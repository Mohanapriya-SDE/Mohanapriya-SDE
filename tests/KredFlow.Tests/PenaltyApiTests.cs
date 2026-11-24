using System.Net;
using System.Net.Http.Json;
using KredFlow.Application.Loans;
using KredFlow.Application.Penalties;
using KredFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace KredFlow.Tests;

public sealed class PenaltyApiTests(ApiFactory factory) : ApiTestBase(factory), IClassFixture<ApiFactory>
{
    private const string Loans = "/api/v1/loans";

    private async Task<(Session Session, Guid LoanId)> LoanAsync(int months = 12, DateOnly? start = null)
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session), months: months, start: start ?? Today().AddDays(-40));
        return (session, loan.Id);
    }

    [Fact]
    public async Task Assess_CreatesAPenaltyAndALedgerEntry()
    {
        var (session, loanId) = await LoanAsync();

        var penalty = await AssessAsync(session, loanId, 250.50m, installment: 1);

        Assert.Equal(250.50m, penalty.Amount);
        Assert.Equal(250.50m, penalty.OutstandingAmount);
        Assert.Equal(0m, penalty.PaidAmount);
        Assert.Equal(0m, penalty.WaivedAmount);
        Assert.Equal(1, penalty.InstallmentNumber);
        Assert.Equal(loanId, penalty.LoanId);
        var ledger = await Factory.WithDbAsync(db => db.LoanTransactions.SingleAsync(t => t.ReferenceId == penalty.Id));
        Assert.Equal(TransactionType.Penalty, ledger.TransactionType);
        Assert.Equal(250.50m, ledger.Amount);
        Assert.Equal(Today(), ledger.TransactionDate);
        var stored = await Factory.WithDbAsync(db => db.Penalties.SingleAsync(p => p.Id == penalty.Id));
        Assert.NotNull(stored.CreatedByUserId);
    }

    [Fact]
    public async Task List_ReturnsPenaltiesOldestFirstWithDerivedAmounts()
    {
        var (session, loanId) = await LoanAsync();
        var newer = await AssessAsync(session, loanId, 100m, date: Today().AddDays(-1));
        var older = await AssessAsync(session, loanId, 200m, date: Today().AddDays(-5));
        await PostWaiverAsync(session, loanId, older.Id, 50m);

        var list = await GetPenaltiesAsync(session, loanId);

        Assert.Equal([older.Id, newer.Id], list.Select(p => p.Id));
        Assert.Equal((200m, 50m, 150m), (list[0].Amount, list[0].WaivedAmount, list[0].OutstandingAmount));
        Assert.Equal((100m, 0m, 100m), (list[1].Amount, list[1].WaivedAmount, list[1].OutstandingAmount));
    }

    [Fact]
    public async Task Assess_RejectsInvalidInput()
    {
        var (session, loanId) = await LoanAsync();
        var url = $"{Loans}/{loanId}/penalties";

        foreach (var amount in new[] { 0m, -1m, 10_000_001m, 10.005m })
        {
            Assert.Equal(HttpStatusCode.BadRequest,
                (await SendAsync(HttpMethod.Post, url, session.Token, new AssessPenaltyRequest { Amount = amount, AssessedDate = Today() })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(HttpMethod.Post, url, session.Token, new AssessPenaltyRequest { Amount = 10m, AssessedDate = Today(), InstallmentNumber = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await SendAsync(HttpMethod.Post, url, session.Token, new AssessPenaltyRequest { Amount = 10m, AssessedDate = Today().AddDays(1) })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await SendAsync(HttpMethod.Post, url, session.Token, new AssessPenaltyRequest { Amount = 10m, AssessedDate = Today().AddDays(-41) })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await SendAsync(HttpMethod.Post, url, session.Token, new AssessPenaltyRequest { Amount = 10m, AssessedDate = Today(), InstallmentNumber = 99 })).StatusCode);
        Assert.Empty(await GetPenaltiesAsync(session, loanId));
    }

    [Fact]
    public async Task AssessAndWaive_AreOwnerOnly()
    {
        var (session, loanId) = await LoanAsync();
        var staff = await StaffTokenAsync(session);
        var penalty = await AssessAsync(session, loanId, 100m);

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Post, $"{Loans}/{loanId}/penalties", staff,
            new AssessPenaltyRequest { Amount = 10m, AssessedDate = Today() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostWaiverAsync(session, loanId, penalty.Id, 10m, token: staff)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, $"{Loans}/{loanId}/penalties", staff)).StatusCode);
    }

    [Fact]
    public async Task Assess_OnAClosedLoan_IsRejected()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session));
        await SendAsync(HttpMethod.Post, $"{Loans}/{loan.Id}/prepayments", session.Token,
            new PrepaymentRequest { Amount = 100_000m, PaymentDate = Today(), Treatment = PrepaymentTreatment.ReduceTenure });
        await PostCloseAsync(session, loan.Id);

        var response = await SendAsync(HttpMethod.Post, $"{Loans}/{loan.Id}/penalties", session.Token,
            new AssessPenaltyRequest { Amount = 10m, AssessedDate = Today() });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Waive_PartialThenRemainder_RecordsAuditableWaivers()
    {
        var (session, loanId) = await LoanAsync();
        var penalty = await AssessAsync(session, loanId, 500m);

        var partial = await ReadAsync<PenaltyResponse>(await PostWaiverAsync(session, loanId, penalty.Id, 200m, reason: "Goodwill"), HttpStatusCode.Created);
        Assert.Equal((200m, 300m), (partial.WaivedAmount, partial.OutstandingAmount));

        var rest = await ReadAsync<PenaltyResponse>(await PostWaiverAsync(session, loanId, penalty.Id, 300m, reason: "Customer hardship"), HttpStatusCode.Created);
        Assert.Equal((500m, 0m), (rest.WaivedAmount, rest.OutstandingAmount));

        var waivers = await Factory.WithDbAsync(db => db.PenaltyWaivers.Where(w => w.PenaltyId == penalty.Id).OrderBy(w => w.CreatedAt).ToListAsync());
        Assert.Equal(["Goodwill", "Customer hardship"], waivers.Select(w => w.Reason));
        Assert.All(waivers, w => Assert.NotNull(w.CreatedByUserId));
        var ledger = await Factory.WithDbAsync(db => db.LoanTransactions
            .Where(t => t.LoanId == loanId && t.TransactionType == TransactionType.Waiver).ToListAsync());
        Assert.Equal(2, ledger.Count);
        Assert.Equal(waivers.Select(w => w.Id).Order(), ledger.Select(t => t.ReferenceId!.Value).Order());
        Assert.Equal(500m, ledger.Sum(t => t.Amount));
    }

    [Fact]
    public async Task Waive_RejectsMoreThanOutstandingAndBadInput()
    {
        var (session, loanId) = await LoanAsync();
        var penalty = await AssessAsync(session, loanId, 100m);
        await PostWaiverAsync(session, loanId, penalty.Id, 60m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostWaiverAsync(session, loanId, penalty.Id, 40.01m)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostWaiverAsync(session, loanId, penalty.Id, 0m)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostWaiverAsync(session, loanId, penalty.Id, 10m, reason: "")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostWaiverAsync(session, loanId, Guid.NewGuid(), 10m)).StatusCode);
        Assert.Equal(40m, Assert.Single(await GetPenaltiesAsync(session, loanId)).OutstandingAmount);

        // A penalty of another loan is not reachable through this loan.
        var other = await CreateLoanAsync(session, await CreateCustomerAsync(session), start: Today().AddDays(-40));
        Assert.Equal(HttpStatusCode.NotFound, (await PostWaiverAsync(session, other.Id, penalty.Id, 10m)).StatusCode);
    }

    [Fact]
    public async Task Waiver_ReducesWhatAPaymentCanSettle()
    {
        var (session, loanId) = await LoanAsync(months: 2);
        var penalty = await AssessAsync(session, loanId, 500m);
        await PostWaiverAsync(session, loanId, penalty.Id, 500m);
        var payable = await TotalRemainingAsync(session, loanId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostPaymentAsync(session, loanId, payable + 1m)).StatusCode);
        var payment = await PayAsync(session, loanId, payable);

        Assert.Equal(0m, payment.PenaltyAllocated);
    }

    [Fact]
    public async Task Close_IsBlockedByAnOutstandingPenalty_UntilItIsPaidOrWaived()
    {
        var (session, loanId) = await LoanAsync(months: 2);
        await PayAsync(session, loanId, await TotalRemainingAsync(session, loanId));
        var penalty = await AssessAsync(session, loanId, 100m);

        var blocked = await PostCloseAsync(session, loanId);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);
        Assert.Contains("penalties 100.00", await blocked.Content.ReadAsStringAsync());

        await PostWaiverAsync(session, loanId, penalty.Id, 40m);
        await PayAsync(session, loanId, 60m);
        Assert.Equal(0m, Assert.Single(await GetPenaltiesAsync(session, loanId)).OutstandingAmount);

        Assert.Equal(HttpStatusCode.OK, (await PostCloseAsync(session, loanId)).StatusCode);
    }
}

public sealed class PenaltyClosureIgnoreTests(IgnorePenaltiesApiFactory factory) : ApiTestBase(factory), IClassFixture<IgnorePenaltiesApiFactory>
{
    [Fact]
    public async Task Close_WithIgnorePenalties_ClosesButKeepsTheOutstandingPenaltyVisible()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session), months: 2, start: Today().AddDays(-40));
        await PayAsync(session, loan.Id, await TotalRemainingAsync(session, loan.Id));
        var penalty = await AssessAsync(session, loan.Id, 100m);

        Assert.Equal(HttpStatusCode.OK, (await PostCloseAsync(session, loan.Id)).StatusCode);

        // Closing did not erase the penalty, its ledger entry, or its outstanding balance.
        var kept = Assert.Single(await GetPenaltiesAsync(session, loan.Id));
        Assert.Equal(penalty.Id, kept.Id);
        Assert.Equal(100m, kept.OutstandingAmount);
        Assert.Equal(1, await Factory.WithDbAsync(db => db.LoanTransactions
            .CountAsync(t => t.LoanId == loan.Id && t.TransactionType == TransactionType.Penalty)));
        Assert.Equal(LoanStatus.Closed, (await GetLoanAsync(session, loan.Id)).Status);
    }
}
