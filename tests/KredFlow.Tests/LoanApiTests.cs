using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KredFlow.Application.Auth;
using KredFlow.Application.Common;
using KredFlow.Application.Customers;
using KredFlow.Application.Loans;
using KredFlow.Domain.Entities;
using KredFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace KredFlow.Tests;

public sealed class LoanApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "StrongPassword@123";
    private const string Base = "/api/v1/loans";

    private HttpClient Client { get; } = factory.CreateClient();

    private sealed record Session(Guid LenderId, string Email, string Token);

    private static DateOnly Today()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
    }

    private async Task<Session> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var register = await Client.PostAsJsonAsync("/api/v1/auth/register", new RegisterLenderRequest
        {
            BusinessName = "ABC Finance",
            OwnerName = "Ravi Kumar",
            Email = email,
            PhoneNumber = "9876543210",
            Password = Password
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var registered = (await register.Content.ReadFromJsonAsync<RegisterLenderResponse>())!;
        return new Session(registered.LenderId, email, await LoginAsync(email));
    }

    private async Task<string> LoginAsync(string email)
    {
        var login = await Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return Client.SendAsync(request);
    }

    private async Task<Guid> CreateCustomerAsync(Session s)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/customers", s.Token,
            new CreateCustomerRequest { FullName = "Meena Devi", PhoneNumber = "9123456789" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CustomerResponse>())!.Id;
    }

    private async Task<LoanResponse> CreateLoanAsync(
        Session s, Guid customerId, decimal principal = 100_000m, decimal rate = 12m, int months = 12,
        DateOnly? start = null, PrepaymentPolicy? policy = null)
    {
        var response = await SendAsync(HttpMethod.Post, Base, s.Token, new CreateLoanRequest
        {
            CustomerId = customerId,
            PrincipalAmount = principal,
            AnnualInterestRate = rate,
            TenureMonths = months,
            StartDate = start ?? Today(),
            PrepaymentPolicy = policy
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoanResponse>())!;
    }

    private async Task<List<RepaymentScheduleResponse>> GetScheduleAsync(Session s, Guid loanId, string query = "")
    {
        var response = await SendAsync(HttpMethod.Get, $"{Base}/{loanId}/schedule{query}", s.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<RepaymentScheduleResponse>>())!;
    }

    private async Task<LoanOutstandingResponse> GetOutstandingAsync(Session s, Guid loanId, string query = "")
    {
        var response = await SendAsync(HttpMethod.Get, $"{Base}/{loanId}/outstanding{query}", s.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoanOutstandingResponse>())!;
    }

    private async Task<LoanResponse> GetLoanAsync(Session s, Guid loanId) =>
        (await (await SendAsync(HttpMethod.Get, $"{Base}/{loanId}", s.Token)).Content.ReadFromJsonAsync<LoanResponse>())!;

    // Records a full payment of the given current installments straight in the database, as the Payment module will later.
    private Task PayInstallmentsAsync(Guid loanId, params int[] numbers) =>
        factory.WithDbAsync(async db =>
        {
            foreach (var n in numbers)
            {
                var row = await db.RepaymentSchedules.SingleAsync(r => r.LoanId == loanId && r.InstallmentNumber == n && !r.IsSuperseded);
                var payment = new Payment
                {
                    Id = Guid.NewGuid(),
                    LoanId = loanId,
                    Amount = row.TotalDue,
                    PaymentDate = Today(),
                    PaymentMethod = PaymentMethod.Cash,
                    Type = PaymentType.Regular,
                    CreatedAt = DateTime.UtcNow
                };
                db.Payments.Add(payment);
                db.PaymentAllocations.Add(new PaymentAllocation
                {
                    Id = Guid.NewGuid(),
                    PaymentId = payment.Id,
                    ScheduleId = row.Id,
                    InterestAmount = row.InterestDue,
                    PrincipalAmount = row.PrincipalDue,
                    Type = AllocationType.Installment
                });
            }

            return await db.SaveChangesAsync();
        });

    // ---------- Create ----------

    [Fact]
    public async Task Create_ReturnsLoanWithCalculatedEmiAndGeneratesSchedule()
    {
        var session = await RegisterAndLoginAsync();
        var customerId = await CreateCustomerAsync(session);

        var response = await SendAsync(HttpMethod.Post, Base, session.Token, new CreateLoanRequest
        {
            CustomerId = customerId,
            PrincipalAmount = 100_000m,
            AnnualInterestRate = 12m,
            TenureMonths = 12,
            StartDate = Today()
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var loan = (await response.Content.ReadFromJsonAsync<LoanResponse>())!;
        Assert.Equal($"{Base}/{loan.Id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal(customerId, loan.CustomerId);
        Assert.Equal(8884.88m, loan.EmiAmount);
        Assert.Equal(LoanStatus.Active, loan.Status);
        Assert.Equal(PrepaymentPolicy.LenderChoice, loan.PrepaymentPolicy);
        Assert.True(loan.ScheduleGenerated);

        var schedule = await GetScheduleAsync(session, loan.Id);
        Assert.Equal(12, schedule.Count);
        Assert.Equal(100_000m, schedule[0].OpeningPrincipal);
        Assert.Equal(1000m, schedule[0].InterestDue);
        Assert.Equal(7884.88m, schedule[0].PrincipalDue);
        Assert.Equal(8884.88m, schedule[0].TotalDue);
        Assert.Equal(0m, schedule[0].PaidAmount);
        Assert.Equal(8884.88m, schedule[0].RemainingAmount);
        Assert.Equal(SchedulePaymentStatus.Upcoming, schedule[0].PaymentStatus);
        Assert.Equal(100_000m, schedule.Sum(r => r.PrincipalDue));

        var stored = await factory.WithDbAsync(db => db.Loans.SingleAsync(l => l.Id == loan.Id));
        Assert.Equal(session.LenderId, stored.LenderId);
        var disbursement = await factory.WithDbAsync(db => db.LoanTransactions.SingleAsync(t => t.LoanId == loan.Id));
        Assert.Equal(TransactionType.Disbursement, disbursement.TransactionType);
        Assert.Equal(100_000m, disbursement.Amount);
    }

    [Fact]
    public async Task Create_IgnoresClientSuppliedEmiLenderAndScheduleRows()
    {
        var session = await RegisterAndLoginAsync();
        var other = await RegisterAndLoginAsync();
        var customerId = await CreateCustomerAsync(session);

        var response = await SendAsync(HttpMethod.Post, Base, session.Token, new
        {
            customerId,
            principalAmount = 100_000,
            annualInterestRate = 12,
            tenureMonths = 12,
            startDate = Today(),
            emiAmount = 1,
            lenderId = other.LenderId,
            schedule = new[] { new { installmentNumber = 1, totalDue = 1 } }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var loan = (await response.Content.ReadFromJsonAsync<LoanResponse>())!;
        Assert.Equal(8884.88m, loan.EmiAmount);
        Assert.Equal(12, (await GetScheduleAsync(session, loan.Id)).Count);
        var stored = await factory.WithDbAsync(db => db.Loans.SingleAsync(l => l.Id == loan.Id));
        Assert.Equal(session.LenderId, stored.LenderId);
    }

    [Fact]
    public async Task Create_UsesRequestedPrepaymentPolicy()
    {
        var session = await RegisterAndLoginAsync();
        var customerId = await CreateCustomerAsync(session);

        var loan = await CreateLoanAsync(session, customerId, policy: PrepaymentPolicy.ReduceEmi);

        Assert.Equal(PrepaymentPolicy.ReduceEmi, loan.PrepaymentPolicy);
    }

    [Theory]
    [InlineData(999, 12, 12, 0)]
    [InlineData(10_000_001, 12, 12, 0)]
    [InlineData(100_000, 0, 12, 0)]
    [InlineData(100_000, 60.01, 12, 0)]
    [InlineData(100_000, 12.123, 12, 0)]
    [InlineData(100_000, 12, 0, 0)]
    [InlineData(100_000, 12, 361, 0)]
    [InlineData(100_000, 12, 12, -366)]
    [InlineData(100_000, 12, 12, 31)]
    public async Task Create_InvalidTerms_ReturnsBadRequestAndSavesNothing(decimal principal, decimal rate, int months, int startOffsetDays)
    {
        var session = await RegisterAndLoginAsync();
        var customerId = await CreateCustomerAsync(session);

        var response = await SendAsync(HttpMethod.Post, Base, session.Token, new CreateLoanRequest
        {
            CustomerId = customerId,
            PrincipalAmount = principal,
            AnnualInterestRate = rate,
            TenureMonths = months,
            StartDate = Today().AddDays(startOffsetDays)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await factory.WithDbAsync(db => db.Loans.CountAsync(l => l.LenderId == session.LenderId)));
    }

    [Fact]
    public async Task Create_AllowsStartDatesAtTheEdgesOfTheWindow()
    {
        var session = await RegisterAndLoginAsync();
        var customerId = await CreateCustomerAsync(session);

        await CreateLoanAsync(session, customerId, start: Today().AddDays(-365));
        await CreateLoanAsync(session, customerId, start: Today().AddDays(30));
    }

    [Fact]
    public async Task Create_CustomerOfAnotherLender_ReturnsNotFound()
    {
        var owner = await RegisterAndLoginAsync();
        var intruder = await RegisterAndLoginAsync();
        var customerId = await CreateCustomerAsync(owner);

        var response = await SendAsync(HttpMethod.Post, Base, intruder.Token, new CreateLoanRequest
        {
            CustomerId = customerId, PrincipalAmount = 50_000m, AnnualInterestRate = 12m, TenureMonths = 6, StartDate = Today()
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_InactiveCustomer_ReturnsUnprocessableEntity()
    {
        var session = await RegisterAndLoginAsync();
        var customerId = await CreateCustomerAsync(session);
        await SendAsync(HttpMethod.Patch, $"/api/v1/customers/{customerId}/status", session.Token, new UpdateCustomerStatusRequest { IsActive = false });

        var response = await SendAsync(HttpMethod.Post, Base, session.Token, new CreateLoanRequest
        {
            CustomerId = customerId, PrincipalAmount = 50_000m, AnnualInterestRate = 12m, TenureMonths = 6, StartDate = Today()
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task AllEndpoints_WithoutToken_ReturnUnauthorized()
    {
        var id = Guid.NewGuid();
        var requests = new (HttpMethod Method, string Url)[]
        {
            (HttpMethod.Post, Base),
            (HttpMethod.Get, $"{Base}/{id}"),
            (HttpMethod.Get, Base),
            (HttpMethod.Get, $"{Base}/{id}/schedule"),
            (HttpMethod.Get, $"{Base}/{id}/outstanding"),
            (HttpMethod.Post, $"{Base}/{id}/close"),
            (HttpMethod.Post, $"{Base}/{id}/prepayments")
        };

        foreach (var (method, url) in requests)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(method, url, token: null, body: new { })).StatusCode);
        }
    }

    // ---------- Read ----------

    [Fact]
    public async Task OtherLender_CannotReadOrChangeLoan()
    {
        var owner = await RegisterAndLoginAsync();
        var intruder = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(owner, await CreateCustomerAsync(owner));

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"{Base}/{loan.Id}", intruder.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"{Base}/{loan.Id}/schedule", intruder.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"{Base}/{loan.Id}/outstanding", intruder.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, $"{Base}/{loan.Id}/close", intruder.Token,
            new CloseLoanRequest { ClosureDate = Today() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, $"{Base}/{loan.Id}/prepayments", intruder.Token,
            new PrepaymentRequest { Amount = 5_000m, PaymentDate = Today(), Treatment = PrepaymentTreatment.ReduceTenure })).StatusCode);

        var list = await SendAsync(HttpMethod.Get, Base, intruder.Token);
        Assert.Empty((await list.Content.ReadFromJsonAsync<PagedResponse<LoanResponse>>())!.Items);
    }

    [Fact]
    public async Task Get_UnknownLoan_ReturnsNotFound()
    {
        var session = await RegisterAndLoginAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"{Base}/{Guid.NewGuid()}", session.Token)).StatusCode);
    }

    [Fact]
    public async Task List_FiltersAndPaginates()
    {
        var session = await RegisterAndLoginAsync();
        var c1 = await CreateCustomerAsync(session);
        var c2 = await CreateCustomerAsync(session);
        var small = await CreateLoanAsync(session, c1, 20_000m, start: Today().AddDays(-300));
        var medium = await CreateLoanAsync(session, c1, 100_000m, start: Today().AddDays(-100));
        var large = await CreateLoanAsync(session, c2, 300_000m, start: Today().AddDays(-10));
        await factory.WithDbAsync(async db =>
        {
            (await db.Loans.SingleAsync(l => l.Id == large.Id)).Status = LoanStatus.Closed;
            return await db.SaveChangesAsync();
        });

        async Task<PagedResponse<LoanResponse>> Get(string query)
        {
            var response = await SendAsync(HttpMethod.Get, Base + query, session.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<PagedResponse<LoanResponse>>())!;
        }

        Assert.Equal([large.Id, medium.Id, small.Id], (await Get("")).Items.Select(l => l.Id));
        Assert.Equal([medium.Id, small.Id], (await Get($"?customerId={c1}")).Items.Select(l => l.Id));
        Assert.Equal([large.Id], (await Get("?status=Closed")).Items.Select(l => l.Id));
        Assert.Equal([medium.Id], (await Get("?minPrincipal=50000&maxPrincipal=200000")).Items.Select(l => l.Id));
        Assert.Equal([medium.Id], (await Get($"?startDateFrom={Today().AddDays(-120):yyyy-MM-dd}&startDateTo={Today().AddDays(-50):yyyy-MM-dd}")).Items.Select(l => l.Id));

        var page = await Get("?page=2&pageSize=2");
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal([small.Id], page.Items.Select(l => l.Id));
        Assert.All((await Get("")).Items, l => Assert.True(l.ScheduleGenerated));
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?status=Bogus")]
    [InlineData("?minPrincipal=200&maxPrincipal=100")]
    [InlineData("?startDateFrom=2026-05-01&startDateTo=2026-04-01")]
    public async Task List_InvalidQuery_ReturnsBadRequest(string query)
    {
        var session = await RegisterAndLoginAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Get, Base + query, session.Token)).StatusCode);
    }

    // ---------- Schedule and outstanding ----------

    [Fact]
    public async Task ScheduleAndOutstanding_AreDerivedFromAllocations()
    {
        var session = await RegisterAndLoginAsync();
        var start = Today().AddDays(-95);
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session), start: start);

        var before = await GetScheduleAsync(session, loan.Id);
        var pastDue = before.Where(r => r.DueDate < Today()).ToList();
        Assert.Equal(3, pastDue.Count);
        Assert.All(pastDue, r => Assert.Equal(SchedulePaymentStatus.Overdue, r.PaymentStatus));

        var outstandingBefore = await GetOutstandingAsync(session, loan.Id);
        Assert.Equal(100_000m, outstandingBefore.PrincipalOutstanding);
        Assert.Equal(pastDue.Sum(r => r.InterestDue), outstandingBefore.AccruedUnpaidInterest);
        Assert.Equal(pastDue.Sum(r => r.TotalDue), outstandingBefore.OverdueAmount);
        Assert.Equal(100_000m + pastDue.Sum(r => r.InterestDue), outstandingBefore.TotalOutstanding);
        Assert.Equal(Today(), outstandingBefore.AsOfDate);

        await PayInstallmentsAsync(loan.Id, 1);

        var after = await GetScheduleAsync(session, loan.Id);
        Assert.Equal(SchedulePaymentStatus.Paid, after[0].PaymentStatus);
        Assert.Equal(after[0].TotalDue, after[0].PaidAmount);
        Assert.Equal(0m, after[0].RemainingAmount);
        Assert.Equal(SchedulePaymentStatus.Overdue, after[1].PaymentStatus);
        Assert.Equal(SchedulePaymentStatus.Upcoming, after[^1].PaymentStatus);

        var outstandingAfter = await GetOutstandingAsync(session, loan.Id);
        Assert.Equal(100_000m - after[0].PrincipalDue, outstandingAfter.PrincipalOutstanding);
        Assert.Equal(pastDue.Skip(1).Sum(r => r.InterestDue), outstandingAfter.AccruedUnpaidInterest);
        Assert.Equal(pastDue.Skip(1).Sum(r => r.TotalDue), outstandingAfter.OverdueAmount);
    }

    [Fact]
    public async Task Outstanding_AsOfDateControlsWhatHasAccrued()
    {
        var session = await RegisterAndLoginAsync();
        var start = Today().AddDays(-95);
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session), start: start);
        var schedule = await GetScheduleAsync(session, loan.Id);

        var atStart = await GetOutstandingAsync(session, loan.Id, $"?asOfDate={start:yyyy-MM-dd}");
        Assert.Equal(0m, atStart.AccruedUnpaidInterest);
        Assert.Equal(0m, atStart.OverdueAmount);
        Assert.Equal(100_000m, atStart.TotalOutstanding);

        var onFirstDueDate = await GetOutstandingAsync(session, loan.Id, $"?asOfDate={schedule[0].DueDate:yyyy-MM-dd}");
        Assert.Equal(schedule[0].InterestDue, onFirstDueDate.AccruedUnpaidInterest);
        Assert.Equal(0m, onFirstDueDate.OverdueAmount);

        var statuses = await GetScheduleAsync(session, loan.Id, $"?asOfDate={schedule[0].DueDate:yyyy-MM-dd}");
        Assert.Equal(SchedulePaymentStatus.Due, statuses[0].PaymentStatus);
        Assert.Equal(SchedulePaymentStatus.Upcoming, statuses[1].PaymentStatus);
    }

    // ---------- Close ----------

    [Fact]
    public async Task Close_WithOutstandingDues_ReturnsUnprocessableEntity()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session));

        var response = await SendAsync(HttpMethod.Post, $"{Base}/{loan.Id}/close", session.Token, new CloseLoanRequest { ClosureDate = Today() });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(LoanStatus.Active, (await GetLoanAsync(session, loan.Id)).Status);
    }

    [Fact]
    public async Task Close_WhenFullyPaid_ClosesOnceAndRejectsSecondClose()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session), months: 3, start: Today().AddDays(-40));
        await PayInstallmentsAsync(loan.Id, 1, 2, 3);

        var response = await SendAsync(HttpMethod.Post, $"{Base}/{loan.Id}/close", session.Token,
            new CloseLoanRequest { ClosureDate = Today(), Remarks = "  All dues settled  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var closed = (await response.Content.ReadFromJsonAsync<LoanResponse>())!;
        Assert.Equal(LoanStatus.Closed, closed.Status);
        Assert.Equal(Today(), closed.ClosedDate);
        var stored = await factory.WithDbAsync(db => db.Loans.SingleAsync(l => l.Id == loan.Id));
        Assert.Equal("All dues settled", stored.ClosureRemarks);

        var again = await SendAsync(HttpMethod.Post, $"{Base}/{loan.Id}/close", session.Token, new CloseLoanRequest { ClosureDate = Today() });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Close_RejectsFutureDateAndDateBeforeLastPayment()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session), months: 2, start: Today().AddDays(-40));
        await PayInstallmentsAsync(loan.Id, 1, 2);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendAsync(HttpMethod.Post, $"{Base}/{loan.Id}/close", session.Token,
            new CloseLoanRequest { ClosureDate = Today().AddDays(1) })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendAsync(HttpMethod.Post, $"{Base}/{loan.Id}/close", session.Token,
            new CloseLoanRequest { ClosureDate = Today().AddDays(-1) })).StatusCode);
    }

    [Fact]
    public async Task Close_IsOwnerOnly()
    {
        var session = await RegisterAndLoginAsync();
        var staffEmail = $"{Guid.NewGuid():N}@example.com";
        await factory.AddStaffUserAsync(session.LenderId, staffEmail, Password);
        var staffToken = await LoginAsync(staffEmail);
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session));

        var response = await SendAsync(HttpMethod.Post, $"{Base}/{loan.Id}/close", staffToken, new CloseLoanRequest { ClosureDate = Today() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- Prepayment ----------

    private async Task<PrepaymentResponse> PrepayAsync(Session s, Guid loanId, decimal amount, PrepaymentTreatment? treatment, HttpStatusCode expected = HttpStatusCode.Created)
    {
        var response = await SendAsync(HttpMethod.Post, $"{Base}/{loanId}/prepayments", s.Token, new PrepaymentRequest
        {
            Amount = amount, PaymentDate = Today(), Treatment = treatment, Remarks = "Additional principal repayment"
        });
        Assert.Equal(expected, response.StatusCode);
        return expected == HttpStatusCode.Created ? (await response.Content.ReadFromJsonAsync<PrepaymentResponse>())! : null!;
    }

    [Fact]
    public async Task Prepayment_ReduceTenure_KeepsEmiAndShortensSchedule()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session));

        var result = await PrepayAsync(session, loan.Id, 20_000m, PrepaymentTreatment.ReduceTenure);

        Assert.Equal(PrepaymentTreatment.ReduceTenure, result.Treatment);
        Assert.Equal(20_000m, result.PrincipalPrepaid);
        Assert.Equal(80_000m, result.PrincipalOutstanding);
        Assert.Equal(8884.88m, result.EmiAmount);
        Assert.True(result.TenureMonths < 12);

        var updated = await GetLoanAsync(session, loan.Id);
        Assert.Equal(result.TenureMonths, updated.TenureMonths);
        Assert.Equal(8884.88m, updated.EmiAmount);
        var schedule = await GetScheduleAsync(session, loan.Id);
        Assert.Equal(result.TenureMonths, schedule.Count);
        Assert.Equal(Enumerable.Range(1, schedule.Count), schedule.Select(r => r.InstallmentNumber));
        Assert.Equal(80_000m, schedule.Sum(r => r.PrincipalDue));
        Assert.Equal(80_000m, (await GetOutstandingAsync(session, loan.Id)).PrincipalOutstanding);

        var payment = await factory.WithDbAsync(db => db.Payments.Include(p => p.Allocations).SingleAsync(p => p.LoanId == loan.Id));
        Assert.Equal(PaymentType.Prepayment, payment.Type);
        var allocation = Assert.Single(payment.Allocations);
        Assert.Null(allocation.ScheduleId);
        Assert.Equal(AllocationType.PrincipalPrepayment, allocation.Type);
        Assert.Equal(1, await factory.WithDbAsync(db => db.LoanTransactions.CountAsync(t => t.LoanId == loan.Id && t.TransactionType == TransactionType.Repayment)));
    }

    [Fact]
    public async Task Prepayment_ReduceEmi_KeepsTenureAndLowersEmi()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session));

        var result = await PrepayAsync(session, loan.Id, 20_000m, PrepaymentTreatment.ReduceEmi);

        Assert.Equal(12, result.TenureMonths);
        Assert.Equal(LoanCalculator.CalculateEmi(80_000m, 12m, 12), result.EmiAmount);
        Assert.True(result.EmiAmount < 8884.88m);
        var schedule = await GetScheduleAsync(session, loan.Id);
        Assert.Equal(12, schedule.Count);
        Assert.Equal(80_000m, schedule.Sum(r => r.PrincipalDue));
        Assert.Equal(result.EmiAmount, (await GetLoanAsync(session, loan.Id)).EmiAmount);
    }

    [Fact]
    public async Task Prepayment_LenderChoiceRequiresTreatment()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session));

        await PrepayAsync(session, loan.Id, 20_000m, treatment: null, expected: HttpStatusCode.BadRequest);

        Assert.Equal(0, await factory.WithDbAsync(db => db.Payments.CountAsync(p => p.LoanId == loan.Id)));
    }

    [Fact]
    public async Task Prepayment_FixedPolicy_DefaultsToItsTreatmentAndRejectsAConflictingOne()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session), policy: PrepaymentPolicy.ReduceTenure);

        await PrepayAsync(session, loan.Id, 5_000m, PrepaymentTreatment.ReduceEmi, HttpStatusCode.UnprocessableEntity);
        var result = await PrepayAsync(session, loan.Id, 5_000m, treatment: null);

        Assert.Equal(PrepaymentTreatment.ReduceTenure, result.Treatment);
    }

    [Fact]
    public async Task Prepayment_WithUnpaidDueInstallments_IsRejected()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session), start: Today().AddDays(-95));

        await PrepayAsync(session, loan.Id, 20_000m, PrepaymentTreatment.ReduceTenure, HttpStatusCode.UnprocessableEntity);

        Assert.Equal(12, (await GetScheduleAsync(session, loan.Id)).Count);
        Assert.Equal(0, await factory.WithDbAsync(db => db.Payments.CountAsync(p => p.LoanId == loan.Id)));
    }

    [Fact]
    public async Task Prepayment_AboveThePayoffAmount_IsRejected()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session));

        await PrepayAsync(session, loan.Id, 100_000.01m, PrepaymentTreatment.ReduceTenure, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Prepayment_AfterInstallmentsPaid_KeepsPaidRowsAndSupersedesTheRest()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session), start: Today().AddDays(-35));
        await PayInstallmentsAsync(loan.Id, 1);
        var firstRow = (await GetScheduleAsync(session, loan.Id))[0];

        var result = await PrepayAsync(session, loan.Id, 20_000m, PrepaymentTreatment.ReduceTenure);

        Assert.Equal(100_000m - firstRow.PrincipalDue - 20_000m, result.PrincipalOutstanding);
        var schedule = await GetScheduleAsync(session, loan.Id);
        Assert.Equal(Enumerable.Range(1, schedule.Count), schedule.Select(r => r.InstallmentNumber));
        Assert.Equal(SchedulePaymentStatus.Paid, schedule[0].PaymentStatus);
        Assert.Equal(firstRow.TotalDue, schedule[0].TotalDue);
        Assert.Equal(result.PrincipalOutstanding, schedule.Skip(1).Sum(r => r.PrincipalDue));
        Assert.Equal(11, await factory.WithDbAsync(db => db.RepaymentSchedules.CountAsync(r => r.LoanId == loan.Id && r.IsSuperseded)));
    }

    [Fact]
    public async Task Prepayment_PayingOffTheWholeBalance_AllowsClosing()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session));

        var result = await PrepayAsync(session, loan.Id, 100_000m, PrepaymentTreatment.ReduceTenure);

        Assert.Equal(0m, result.PrincipalOutstanding);
        Assert.Empty(await GetScheduleAsync(session, loan.Id));
        Assert.True((await GetLoanAsync(session, loan.Id)).ScheduleGenerated);
        Assert.Equal(12, (await GetLoanAsync(session, loan.Id)).TenureMonths);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, $"{Base}/{loan.Id}/close", session.Token,
            new CloseLoanRequest { ClosureDate = Today() })).StatusCode);
    }

    [Fact]
    public async Task Prepayment_OnClosedLoanOrWithBadDates_IsRejected()
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session));

        foreach (var date in new[] { Today().AddDays(1), Today().AddDays(-1) })
        {
            var response = await SendAsync(HttpMethod.Post, $"{Base}/{loan.Id}/prepayments", session.Token,
                new PrepaymentRequest { Amount = 5_000m, PaymentDate = date, Treatment = PrepaymentTreatment.ReduceTenure });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        }

        await PrepayAsync(session, loan.Id, 100_000m, PrepaymentTreatment.ReduceTenure);
        await SendAsync(HttpMethod.Post, $"{Base}/{loan.Id}/close", session.Token, new CloseLoanRequest { ClosureDate = Today() });
        await PrepayAsync(session, loan.Id, 1_000m, PrepaymentTreatment.ReduceTenure, HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Prepayment_NonPositiveAmount_ReturnsBadRequest(decimal amount)
    {
        var session = await RegisterAndLoginAsync();
        var loan = await CreateLoanAsync(session, await CreateCustomerAsync(session));

        await PrepayAsync(session, loan.Id, amount, PrepaymentTreatment.ReduceTenure, HttpStatusCode.BadRequest);
    }
}
