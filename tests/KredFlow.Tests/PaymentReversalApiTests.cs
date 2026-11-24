using System.Net;
using System.Net.Http.Json;
using KredFlow.Application.Common;
using KredFlow.Application.Loans;
using KredFlow.Application.Payments;
using KredFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace KredFlow.Tests;

public sealed class PaymentReversalApiTests(ApiFactory factory) : ApiTestBase(factory), IClassFixture<ApiFactory>
{
    private const string Loans = "/api/v1/loans";

    private Task<HttpResponseMessage> PrepayAsync(Session s, Guid loanId, decimal amount) =>
        SendAsync(HttpMethod.Post, $"{Loans}/{loanId}/prepayments", s.Token,
            new PrepaymentRequest { Amount = amount, PaymentDate = Today(), Treatment = PrepaymentTreatment.ReduceTenure });

    [Fact]
    public async Task Reverse_RestoresOutstandingScheduleAndPenaltyBalances_AndKeepsTheOriginalPayment()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session), start: Today().AddDays(-95));
        var schedule = await GetScheduleAsync(session, loan.Id);
        var interest = schedule.Where(r => r.DueDate < Today()).Sum(r => r.InterestDue);
        await AssessAsync(session, loan.Id, 500m);
        var outstandingBefore = await GetOutstandingAsync(session, loan.Id);
        var payment = await PayAsync(session, loan.Id, interest + 500m + 1_000m);
        Assert.NotEqual(outstandingBefore.PrincipalOutstanding, (await GetOutstandingAsync(session, loan.Id)).PrincipalOutstanding);

        var reversed = await ReverseAsync(session, payment.Id);

        Assert.Equal(PaymentStatus.Reversed, reversed.Status);
        Assert.Equal("Cheque bounced", reversed.Reversal!.Reason);
        Assert.Equal(Today(), reversed.Reversal.ReversalDate);
        Assert.Equal(payment.Allocations.Count, reversed.Allocations.Count);

        var outstandingAfter = await GetOutstandingAsync(session, loan.Id);
        Assert.Equal(outstandingBefore.PrincipalOutstanding, outstandingAfter.PrincipalOutstanding);
        Assert.Equal(outstandingBefore.AccruedUnpaidInterest, outstandingAfter.AccruedUnpaidInterest);
        Assert.Equal(outstandingBefore.OverdueAmount, outstandingAfter.OverdueAmount);
        Assert.Equal(outstandingBefore.TotalOutstanding, outstandingAfter.TotalOutstanding);

        var restored = await GetScheduleAsync(session, loan.Id);
        Assert.All(restored.Take(3), r =>
        {
            Assert.Equal(0m, r.PaidAmount);
            Assert.Equal(SchedulePaymentStatus.Overdue, r.PaymentStatus);
        });
        var penalty = Assert.Single(await GetPenaltiesAsync(session, loan.Id));
        Assert.Equal(0m, penalty.PaidAmount);
        Assert.Equal(500m, penalty.OutstandingAmount);

        // Nothing is deleted: the payment, its allocations, the reversal and both ledger entries all remain.
        Assert.Equal(PaymentStatus.Reversed,
            (await ReadAsync<PaymentResponse>(await SendAsync(HttpMethod.Get, $"/api/v1/payments/{payment.Id}", session.Token), HttpStatusCode.OK)).Status);
        Assert.Equal(payment.Allocations.Count, await Factory.WithDbAsync(db => db.PaymentAllocations.CountAsync(a => a.PaymentId == payment.Id)));
        Assert.Equal(1, await Factory.WithDbAsync(db => db.PaymentReversals.CountAsync(r => r.PaymentId == payment.Id)));
        var ledger = await Factory.WithDbAsync(db => db.LoanTransactions.Where(t => t.ReferenceId == payment.Id).ToListAsync());
        Assert.Equal([TransactionType.Repayment, TransactionType.Reversal], ledger.Select(t => t.TransactionType).OrderBy(t => t));
        Assert.All(ledger, t => Assert.Equal(payment.Amount, t.Amount));
    }

    [Fact]
    public async Task Reverse_FreesTheLastPaymentDate_SoAnEarlierPaymentCanBeRecorded()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session), start: Today().AddDays(-10));
        var payment = await PayAsync(session, loan.Id, 100m);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostPaymentAsync(session, loan.Id, 100m, date: Today().AddDays(-1))).StatusCode);

        await ReverseAsync(session, payment.Id);

        Assert.Equal(HttpStatusCode.Created, (await PostPaymentAsync(session, loan.Id, 100m, date: Today().AddDays(-1))).StatusCode);
    }

    [Fact]
    public async Task Reverse_ARepeatedReversalIsAConflict_AndRulesAreEnforced()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session), start: Today().AddDays(-95));
        var payment = await PayAsync(session, loan.Id, 100m);
        var staff = await StaffTokenAsync(session);

        Assert.Equal(HttpStatusCode.Forbidden, (await PostReversalAsync(session, payment.Id, token: staff)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostReversalAsync(session, payment.Id, reason: "")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostReversalAsync(session, payment.Id, reason: new string('x', 501))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostReversalAsync(session, payment.Id, date: Today().AddDays(1))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostReversalAsync(session, payment.Id, date: Today().AddDays(-1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostReversalAsync(session, Guid.NewGuid())).StatusCode);
        Assert.Equal(0, await Factory.WithDbAsync(db => db.PaymentReversals.CountAsync(r => r.PaymentId == payment.Id)));

        await ReverseAsync(session, payment.Id);

        Assert.Equal(HttpStatusCode.Conflict, (await PostReversalAsync(session, payment.Id)).StatusCode);
        Assert.Equal(1, await Factory.WithDbAsync(db => db.PaymentReversals.CountAsync(r => r.PaymentId == payment.Id)));
    }

    [Fact]
    public async Task Reverse_ARegularPaymentCanBeReversedOutOfOrder_AndLaterPaymentsKeepTheirAllocations()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session), start: Today().AddDays(-95));
        var first = await PayAsync(session, loan.Id, 1_000m);
        var second = await PayAsync(session, loan.Id, 2_000m);
        Assert.True(second.PrincipalAllocated > 0);

        await ReverseAsync(session, first.Id);

        // Only the second payment still counts, exactly as it was allocated when it was recorded.
        var schedule = await GetScheduleAsync(session, loan.Id);
        Assert.Equal(2_000m, schedule.Sum(r => r.PaidAmount));
        Assert.Equal(100_000m - second.PrincipalAllocated, (await GetOutstandingAsync(session, loan.Id)).PrincipalOutstanding);
    }

    [Fact]
    public async Task ReversePrepayment_RestoresTheOriginalScheduleEmiAndTenure()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session));
        var original = await GetScheduleAsync(session, loan.Id);
        var prepayment = await ReadAsync<PrepaymentResponse>(await PrepayAsync(session, loan.Id, 20_000m), HttpStatusCode.Created);
        Assert.Equal(10, prepayment.TenureMonths);

        var reversed = await ReverseAsync(session, prepayment.PaymentId);

        Assert.Equal(PaymentStatus.Reversed, reversed.Status);
        Assert.Equal(PaymentType.Prepayment, reversed.Type);
        var updated = await GetLoanAsync(session, loan.Id);
        Assert.Equal(12, updated.TenureMonths);
        Assert.Equal(8884.88m, updated.EmiAmount);
        var restored = await GetScheduleAsync(session, loan.Id);
        Assert.Equal(12, restored.Count);
        Assert.Equal(original.Select(r => (r.InstallmentNumber, r.DueDate, r.PrincipalDue, r.InterestDue)),
            restored.Select(r => (r.InstallmentNumber, r.DueDate, r.PrincipalDue, r.InterestDue)));
        Assert.Equal(100_000m, (await GetOutstandingAsync(session, loan.Id)).PrincipalOutstanding);

        // Rows generated by the prepayment are retired, not deleted, and numbering can be reused again.
        Assert.Equal(22, await Factory.WithDbAsync(db => db.RepaymentSchedules.CountAsync(r => r.LoanId == loan.Id)));
        Assert.Equal(HttpStatusCode.Created, (await PrepayAsync(session, loan.Id, 20_000m)).StatusCode);
        Assert.Equal(10, (await GetScheduleAsync(session, loan.Id)).Count);
    }

    [Fact]
    public async Task ReversePrepayment_RestoresTheEmiForAReduceEmiRecast()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session));
        var prepayment = await ReadAsync<PrepaymentResponse>(
            await SendAsync(HttpMethod.Post, $"{Loans}/{loan.Id}/prepayments", session.Token,
                new PrepaymentRequest { Amount = 20_000m, PaymentDate = Today(), Treatment = PrepaymentTreatment.ReduceEmi }),
            HttpStatusCode.Created);
        Assert.True(prepayment.EmiAmount < 8884.88m);

        await ReverseAsync(session, prepayment.PaymentId);

        var updated = await GetLoanAsync(session, loan.Id);
        Assert.Equal(8884.88m, updated.EmiAmount);
        Assert.Equal(12, updated.TenureMonths);
    }

    [Fact]
    public async Task Reverse_IsBlockedWhenALaterPaymentDependsOnIt()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session));

        // A regular payment followed by a prepayment: the prepayment depends on the regular payment.
        var regular = await PayAsync(session, loan.Id, 1_000m);
        var prepayment = await ReadAsync<PrepaymentResponse>(await PrepayAsync(session, loan.Id, 10_000m), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostReversalAsync(session, regular.Id)).StatusCode);

        // A prepayment followed by another payment can only be reversed after that payment is.
        var later = await PayAsync(session, loan.Id, 500m);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostReversalAsync(session, prepayment.PaymentId)).StatusCode);

        await ReverseAsync(session, later.Id);
        await ReverseAsync(session, prepayment.PaymentId);
        await ReverseAsync(session, regular.Id);

        Assert.Equal(100_000m, (await GetOutstandingAsync(session, loan.Id)).PrincipalOutstanding);
        Assert.Equal(12, (await GetLoanAsync(session, loan.Id)).TenureMonths);
    }

    [Fact]
    public async Task Reverse_IsRejectedOnAClosedLoan()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session));
        var prepayment = await ReadAsync<PrepaymentResponse>(await PrepayAsync(session, loan.Id, 100_000m), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.OK, (await PostCloseAsync(session, loan.Id)).StatusCode);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostReversalAsync(session, prepayment.PaymentId)).StatusCode);
    }

    [Fact]
    public async Task Reverse_APrepaymentMadeBeforeRecastTrackingIsRejected()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session));
        var prepayment = await ReadAsync<PrepaymentResponse>(await PrepayAsync(session, loan.Id, 20_000m), HttpStatusCode.Created);
        await Factory.WithDbAsync(async db =>
        {
            db.LoanRecasts.RemoveRange(db.LoanRecasts.Where(r => r.PaymentId == prepayment.PaymentId));
            return await db.SaveChangesAsync();
        });

        var response = await PostReversalAsync(session, prepayment.PaymentId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("recast", await response.Content.ReadAsStringAsync());
    }
}
