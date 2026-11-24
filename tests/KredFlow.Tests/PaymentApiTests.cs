using System.Net;
using System.Net.Http.Json;
using KredFlow.Application.Common;
using KredFlow.Application.Customers;
using KredFlow.Application.Loans;
using KredFlow.Application.Payments;
using KredFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace KredFlow.Tests;

public sealed class PaymentApiTests(ApiFactory factory) : ApiTestBase(factory), IClassFixture<ApiFactory>
{
    private const string Loans = "/api/v1/loans";

    // A loan that started 95 days ago has three installments past due.
    private async Task<(Session Session, Guid CustomerId, LoanResponse Loan, List<RepaymentScheduleResponse> Schedule)> OverdueLoanAsync()
    {
        var session = await RegisterAndLoginAsync();
        var customerId = await CreateCustomerAsync(session);
        var loan = await CreateLoanAsync(session, customerId, start: Today().AddDays(-95));
        return (session, customerId, loan, await GetScheduleAsync(session, loan.Id));
    }

    [Fact]
    public async Task Record_AllocatesInterestThenPenaltyThenPrincipal()
    {
        var (session, _, loan, schedule) = await OverdueLoanAsync();
        var due = schedule.Where(r => r.DueDate < Today()).ToList();
        var interest = due.Sum(r => r.InterestDue);
        await AssessAsync(session, loan.Id, 500m);
        var before = await GetOutstandingAsync(session, loan.Id);

        var response = await PostPaymentAsync(session, loan.Id, interest + 500m + 1_000m, key: "pay-1", method: PaymentMethod.UPI);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payment = (await response.Content.ReadFromJsonAsync<PaymentResponse>())!;
        Assert.Equal($"/api/v1/payments/{payment.Id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal(PaymentStatus.Recorded, payment.Status);
        Assert.Equal(PaymentType.Regular, payment.Type);
        Assert.Equal(PaymentMethod.UPI, payment.PaymentMethod);
        Assert.Equal("REF-1", payment.ReferenceNumber);
        Assert.Equal(loan.CustomerId, payment.CustomerId);
        Assert.Equal(interest, payment.InterestAllocated);
        Assert.Equal(500m, payment.PenaltyAllocated);
        Assert.Equal(1_000m, payment.PrincipalAllocated);

        Assert.Equal([AllocationType.Installment, AllocationType.Installment, AllocationType.Installment, AllocationType.Penalty],
            payment.Allocations.Select(a => a.AllocationType));
        Assert.Equal([1, 2, 3], payment.Allocations.Where(a => a.InstallmentNumber is not null).Select(a => a.InstallmentNumber!.Value));
        Assert.Equal(1_000m, payment.Allocations[0].PrincipalAmount);
        Assert.Equal(0m, payment.Allocations[1].PrincipalAmount);

        var after = await GetOutstandingAsync(session, loan.Id);
        Assert.Equal(before.PrincipalOutstanding - 1_000m, after.PrincipalOutstanding);
        Assert.Equal(0m, after.AccruedUnpaidInterest);
        Assert.Equal(before.OverdueAmount - interest - 1_000m, after.OverdueAmount);

        var penalty = Assert.Single(await GetPenaltiesAsync(session, loan.Id));
        Assert.Equal(500m, penalty.PaidAmount);
        Assert.Equal(0m, penalty.OutstandingAmount);

        var ledger = await Factory.WithDbAsync(db => db.LoanTransactions.SingleAsync(t => t.ReferenceId == payment.Id));
        Assert.Equal(TransactionType.Repayment, ledger.TransactionType);
        Assert.Equal(payment.Amount, ledger.Amount);

        var fetched = await ReadAsync<PaymentResponse>(await SendAsync(HttpMethod.Get, $"/api/v1/payments/{payment.Id}", session.Token), HttpStatusCode.OK);
        Assert.Equal(payment.Id, fetched.Id);
        Assert.Equal(4, fetched.Allocations.Count);
    }

    [Fact]
    public async Task Record_PartialPayment_FillsInterestOldestFirstBeforeAnythingElse()
    {
        var (session, _, loan, schedule) = await OverdueLoanAsync();
        await AssessAsync(session, loan.Id, 500m);

        var payment = await PayAsync(session, loan.Id, 100m);

        var line = Assert.Single(payment.Allocations);
        Assert.Equal(1, line.InstallmentNumber);
        Assert.Equal(100m, line.InterestAmount);
        Assert.Equal(0m, Assert.Single(await GetPenaltiesAsync(session, loan.Id)).PaidAmount);
        Assert.True(schedule[0].InterestDue > 100m);
    }

    [Fact]
    public async Task Record_PartialPenaltyPayment_LeavesTheRestOutstanding()
    {
        var (session, _, loan, schedule) = await OverdueLoanAsync();
        var interest = schedule.Where(r => r.DueDate < Today()).Sum(r => r.InterestDue);
        await AssessAsync(session, loan.Id, 500m);

        var payment = await PayAsync(session, loan.Id, interest + 200m);

        Assert.Equal(200m, payment.PenaltyAllocated);
        Assert.Equal(0m, payment.PrincipalAllocated);
        var penalty = Assert.Single(await GetPenaltiesAsync(session, loan.Id));
        Assert.Equal(200m, penalty.PaidAmount);
        Assert.Equal(300m, penalty.OutstandingAmount);
    }

    [Fact]
    public async Task Record_ExcessOverWhatIsDue_AdvancesUpcomingInstallmentsWithoutRecasting()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session));

        var payment = await PayAsync(session, loan.Id, 8884.88m + 100m);

        Assert.Equal(2, payment.Allocations.Count);
        Assert.Equal((1000m, 7884.88m), (payment.Allocations[0].InterestAmount, payment.Allocations[0].PrincipalAmount));
        Assert.Equal((100m, 0m), (payment.Allocations[1].InterestAmount, payment.Allocations[1].PrincipalAmount));
        var schedule = await GetScheduleAsync(session, loan.Id);
        Assert.Equal(SchedulePaymentStatus.Paid, schedule[0].PaymentStatus);
        Assert.Equal(SchedulePaymentStatus.Partial, schedule[1].PaymentStatus);
        Assert.Equal(100m, schedule[1].PaidAmount);
        var updated = await GetLoanAsync(session, loan.Id);
        Assert.Equal(12, updated.TenureMonths);
        Assert.Equal(8884.88m, updated.EmiAmount);
    }

    [Fact]
    public async Task Record_CannotExceedTheTotalStillPayable()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session), months: 3);
        var payable = await TotalRemainingAsync(session, loan.Id);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostPaymentAsync(session, loan.Id, payable + 0.01m)).StatusCode);
        Assert.Equal(0, await Factory.WithDbAsync(db => db.Payments.CountAsync(p => p.LoanId == loan.Id)));

        await PayAsync(session, loan.Id, payable);
        Assert.Equal(0m, (await GetOutstandingAsync(session, loan.Id)).PrincipalOutstanding);
        Assert.All(await GetScheduleAsync(session, loan.Id), r => Assert.Equal(SchedulePaymentStatus.Paid, r.PaymentStatus));
    }

    [Fact]
    public async Task Record_InvalidRequest_ReturnsBadRequestAndSavesNothing()
    {
        var (session, _, loan, _) = await OverdueLoanAsync();

        foreach (var (amount, key) in new[] { (0m, "k"), (-5m, "k"), (10.005m, "k"), (100m, ""), (100m, new string('x', 101)) })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await PostPaymentAsync(session, loan.Id, amount, key: key)).StatusCode);
        }

        var noDate = await SendAsync(HttpMethod.Post, $"{Loans}/{loan.Id}/payments", session.Token,
            new { amount = 100, paymentMethod = "Cash", idempotencyKey = "k" });
        Assert.Equal(HttpStatusCode.BadRequest, noDate.StatusCode);
        Assert.Equal(0, await Factory.WithDbAsync(db => db.Payments.CountAsync(p => p.LoanId == loan.Id)));
    }

    [Fact]
    public async Task Record_RejectsFutureBeforeStartAndBeforeLastPaymentDates()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session), start: Today().AddDays(-10));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostPaymentAsync(session, loan.Id, 100m, date: Today().AddDays(1))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostPaymentAsync(session, loan.Id, 100m, date: Today().AddDays(-11))).StatusCode);

        await PayAsync(session, loan.Id, 100m, date: Today().AddDays(-2));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostPaymentAsync(session, loan.Id, 100m, date: Today().AddDays(-3))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostPaymentAsync(session, loan.Id, 100m, date: Today().AddDays(-2))).StatusCode);
    }

    [Fact]
    public async Task Record_OnClosedLoan_IsRejected()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session));
        await SendAsync(HttpMethod.Post, $"{Loans}/{loan.Id}/prepayments", session.Token,
            new PrepaymentRequest { Amount = 100_000m, PaymentDate = Today(), Treatment = PrepaymentTreatment.ReduceTenure });
        Assert.Equal(HttpStatusCode.OK, (await PostCloseAsync(session, loan.Id)).StatusCode);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostPaymentAsync(session, loan.Id, 100m)).StatusCode);
    }

    [Fact]
    public async Task Record_IsIdempotent_ForTheSameKeyAndRequest()
    {
        var (session, customerId, loan, _) = await OverdueLoanAsync();

        var first = await ReadAsync<PaymentResponse>(await PostPaymentAsync(session, loan.Id, 500m, key: "same-key"), HttpStatusCode.Created);
        var replay = await ReadAsync<PaymentResponse>(await PostPaymentAsync(session, loan.Id, 500m, key: "same-key"), HttpStatusCode.OK);

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(first.Allocations.Count, replay.Allocations.Count);
        Assert.Equal(1, await Factory.WithDbAsync(db => db.Payments.CountAsync(p => p.LoanId == loan.Id)));
        Assert.Equal(1, await Factory.WithDbAsync(db => db.LoanTransactions.CountAsync(t => t.ReferenceId == first.Id)));

        // The same key with a different request is a client error, never a second posting.
        Assert.Equal(HttpStatusCode.Conflict, (await PostPaymentAsync(session, loan.Id, 600m, key: "same-key")).StatusCode);
        Assert.Equal(1, await Factory.WithDbAsync(db => db.Payments.CountAsync(p => p.LoanId == loan.Id)));

        // Keys are scoped to a loan.
        var other = await CreateLoanAsync(session, customerId, start: Today().AddDays(-95));
        Assert.Equal(HttpStatusCode.Created, (await PostPaymentAsync(session, other.Id, 500m, key: "same-key")).StatusCode);
    }

    [Fact]
    public async Task Record_ReplayStillWorksAfterTheLoanIsClosed()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session), months: 2);
        var payable = await TotalRemainingAsync(session, loan.Id);
        var first = await ReadAsync<PaymentResponse>(await PostPaymentAsync(session, loan.Id, payable, key: "final"), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.OK, (await PostCloseAsync(session, loan.Id)).StatusCode);

        var replay = await ReadAsync<PaymentResponse>(await PostPaymentAsync(session, loan.Id, payable, key: "final"), HttpStatusCode.OK);

        Assert.Equal(first.Id, replay.Id);
    }

    [Fact]
    public async Task AllEndpoints_WithoutToken_ReturnUnauthorized()
    {
        var id = Guid.NewGuid();
        var requests = new (HttpMethod Method, string Url)[]
        {
            (HttpMethod.Post, $"{Loans}/{id}/payments"),
            (HttpMethod.Get, $"/api/v1/payments/{id}"),
            (HttpMethod.Get, $"{Loans}/{id}/payments"),
            (HttpMethod.Get, $"/api/v1/customers/{id}/payments"),
            (HttpMethod.Post, $"/api/v1/payments/{id}/reversals"),
            (HttpMethod.Get, $"{Loans}/{id}/penalties"),
            (HttpMethod.Post, $"{Loans}/{id}/penalties"),
            (HttpMethod.Post, $"{Loans}/{id}/penalties/{id}/waivers")
        };

        foreach (var (method, url) in requests)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(method, url, token: null, body: new { })).StatusCode);
        }
    }

    [Fact]
    public async Task OtherLender_CannotSeeOrUsePaymentsAndPenalties()
    {
        var (owner, customerId, loan, _) = await OverdueLoanAsync();
        var intruder = await RegisterAndLoginAsync();
        var payment = await PayAsync(owner, loan.Id, 100m);
        var penalty = await AssessAsync(owner, loan.Id, 50m);

        Assert.Equal(HttpStatusCode.NotFound, (await PostPaymentAsync(intruder, loan.Id, 100m)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/v1/payments/{payment.Id}", intruder.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostReversalAsync(intruder, payment.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"{Loans}/{loan.Id}/payments", intruder.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/v1/customers/{customerId}/payments", intruder.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"{Loans}/{loan.Id}/penalties", intruder.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, $"{Loans}/{loan.Id}/penalties", intruder.Token,
            new { amount = 10, assessedDate = Today() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostWaiverAsync(intruder, loan.Id, penalty.Id, 10m)).StatusCode);

        Assert.Equal(1, await Factory.WithDbAsync(db => db.Payments.CountAsync(p => p.LoanId == loan.Id)));
    }

    [Fact]
    public async Task LoanHistory_IsPagedFilteredAndNewestFirst()
    {
        var (session, _, loan, _) = await OverdueLoanAsync();
        var a = await PayAsync(session, loan.Id, 1_000m, date: Today().AddDays(-1));
        var b = await PayAsync(session, loan.Id, 2_000m, method: PaymentMethod.UPI);
        await ReverseAsync(session, a.Id);

        async Task<PagedResponse<PaymentSummaryResponse>> Get(string query) =>
            await ReadAsync<PagedResponse<PaymentSummaryResponse>>(
                await SendAsync(HttpMethod.Get, $"{Loans}/{loan.Id}/payments{query}", session.Token), HttpStatusCode.OK);

        Assert.Equal([b.Id, a.Id], (await Get("")).Items.Select(p => p.Id));
        Assert.Equal([a.Id], (await Get("?status=Reversed")).Items.Select(p => p.Id));
        Assert.Equal(PaymentStatus.Reversed, (await Get("?status=Reversed")).Items[0].Status);
        Assert.Equal([b.Id], (await Get("?status=Recorded")).Items.Select(p => p.Id));
        Assert.Equal([b.Id], (await Get("?paymentMethod=UPI")).Items.Select(p => p.Id));
        Assert.Equal([a.Id], (await Get($"?from={Today().AddDays(-2):yyyy-MM-dd}&to={Today().AddDays(-1):yyyy-MM-dd}")).Items.Select(p => p.Id));

        var page = await Get("?page=2&pageSize=1");
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal([a.Id], page.Items.Select(p => p.Id));
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?status=Bogus")]
    [InlineData("?from=2026-05-01&to=2026-04-01")]
    public async Task History_InvalidQuery_ReturnsBadRequest(string query)
    {
        var (session, customerId, loan, _) = await OverdueLoanAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Get, $"{Loans}/{loan.Id}/payments{query}", session.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Get, $"/api/v1/customers/{customerId}/payments{query}", session.Token)).StatusCode);
    }

    [Fact]
    public async Task CustomerHistory_CoversAllOfTheCustomersLoansAndNoOneElses()
    {
        var (session, customerId, loan, _) = await OverdueLoanAsync();
        var second = await CreateLoanAsync(session, customerId, start: Today().AddDays(-95));
        var otherCustomer = await ReadAsync<CustomerResponse>(
            await SendAsync(HttpMethod.Post, "/api/v1/customers", session.Token, new CreateCustomerRequest { FullName = "Other", PhoneNumber = "9000000001" }),
            HttpStatusCode.Created);
        var unrelated = await CreateLoanAsync(session, otherCustomer.Id, start: Today().AddDays(-95));
        var p1 = await PayAsync(session, loan.Id, 500m);
        var p2 = await PayAsync(session, second.Id, 700m);
        await PayAsync(session, unrelated.Id, 900m);

        var page = await ReadAsync<PagedResponse<PaymentSummaryResponse>>(
            await SendAsync(HttpMethod.Get, $"/api/v1/customers/{customerId}/payments", session.Token), HttpStatusCode.OK);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(new HashSet<Guid> { p1.Id, p2.Id }, page.Items.Select(p => p.Id).ToHashSet());
        Assert.All(page.Items, p => Assert.Equal(customerId, p.CustomerId));

        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(HttpMethod.Get, $"/api/v1/customers/{Guid.NewGuid()}/payments", session.Token)).StatusCode);
    }

    [Fact]
    public async Task Prepayment_IsRejectedWhilePenaltiesAreOutstanding_AndAllowedOnceWaived()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session), start: Today().AddDays(-35));
        var firstRow = (await GetScheduleAsync(session, loan.Id))[0];
        await PayAsync(session, loan.Id, firstRow.TotalDue);
        var penalty = await AssessAsync(session, loan.Id, 100m);
        var request = new PrepaymentRequest { Amount = 5_000m, PaymentDate = Today(), Treatment = PrepaymentTreatment.ReduceTenure };

        var rejected = await SendAsync(HttpMethod.Post, $"{Loans}/{loan.Id}/prepayments", session.Token, request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.Contains("Penalties", await rejected.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, (await PostWaiverAsync(session, loan.Id, penalty.Id, 100m)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(HttpMethod.Post, $"{Loans}/{loan.Id}/prepayments", session.Token, request)).StatusCode);
    }

    [Fact]
    public async Task Prepayment_IsIdempotent_WhenAKeyIsSupplied()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session));
        var body = new PrepaymentRequest { Amount = 20_000m, PaymentDate = Today(), Treatment = PrepaymentTreatment.ReduceTenure, IdempotencyKey = "pre-1" };

        var first = await ReadAsync<PrepaymentResponse>(await SendAsync(HttpMethod.Post, $"{Loans}/{loan.Id}/prepayments", session.Token, body), HttpStatusCode.Created);
        var replay = await ReadAsync<PrepaymentResponse>(await SendAsync(HttpMethod.Post, $"{Loans}/{loan.Id}/prepayments", session.Token, body), HttpStatusCode.OK);

        Assert.Equal(first.PaymentId, replay.PaymentId);
        Assert.Equal(first.TenureMonths, replay.TenureMonths);
        Assert.Equal(first.PrincipalOutstanding, replay.PrincipalOutstanding);
        Assert.Equal(first.EmiAmount, replay.EmiAmount);
        Assert.Equal(1, await Factory.WithDbAsync(db => db.Payments.CountAsync(p => p.LoanId == loan.Id)));

        body.Amount = 30_000m;
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(HttpMethod.Post, $"{Loans}/{loan.Id}/prepayments", session.Token, body)).StatusCode);
        Assert.Equal(80_000m, (await GetOutstandingAsync(session, loan.Id)).PrincipalOutstanding);
    }
}
