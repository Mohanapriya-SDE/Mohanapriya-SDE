using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KredFlow.Application.Auth;
using KredFlow.Application.Customers;
using KredFlow.Application.Loans;
using KredFlow.Application.Payments;
using KredFlow.Application.Penalties;
using KredFlow.Domain.Enums;

namespace KredFlow.Tests;

/// <summary>Shared HTTP helpers for tests that drive the real API: lenders, customers, loans, payments and penalties.</summary>
public abstract class ApiTestBase(ApiFactory factory)
{
    protected const string Password = "StrongPassword@123";

    protected ApiFactory Factory { get; } = factory;
    protected HttpClient Client { get; } = factory.CreateClient();

    protected sealed record Session(Guid LenderId, string Email, string Token);

    protected static DateOnly Today()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
    }

    protected async Task<Session> RegisterAndLoginAsync()
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

    protected async Task<string> LoginAsync(string email)
    {
        var login = await Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    protected async Task<string> StaffTokenAsync(Session owner)
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        await Factory.AddStaffUserAsync(owner.LenderId, email, Password);
        return await LoginAsync(email);
    }

    protected Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? token, object? body = null)
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

    protected async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    protected async Task<Guid> CreateCustomerAsync(Session s)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/customers", s.Token,
            new CreateCustomerRequest { FullName = "Meena Devi", PhoneNumber = "9123456789" });
        return (await ReadAsync<CustomerResponse>(response, HttpStatusCode.Created)).Id;
    }

    protected async Task<LoanResponse> CreateLoanAsync(
        Session s, Guid customerId, decimal principal = 100_000m, decimal rate = 12m, int months = 12,
        DateOnly? start = null, PrepaymentPolicy? policy = null)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/loans", s.Token, new CreateLoanRequest
        {
            CustomerId = customerId,
            PrincipalAmount = principal,
            AnnualInterestRate = rate,
            TenureMonths = months,
            StartDate = start ?? Today(),
            PrepaymentPolicy = policy
        });
        return await ReadAsync<LoanResponse>(response, HttpStatusCode.Created);
    }

    protected async Task<List<RepaymentScheduleResponse>> GetScheduleAsync(Session s, Guid loanId)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/loans/{loanId}/schedule", s.Token);
        return await ReadAsync<List<RepaymentScheduleResponse>>(response, HttpStatusCode.OK);
    }

    protected async Task<LoanOutstandingResponse> GetOutstandingAsync(Session s, Guid loanId)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/loans/{loanId}/outstanding", s.Token);
        return await ReadAsync<LoanOutstandingResponse>(response, HttpStatusCode.OK);
    }

    protected async Task<LoanResponse> GetLoanAsync(Session s, Guid loanId)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/loans/{loanId}", s.Token);
        return await ReadAsync<LoanResponse>(response, HttpStatusCode.OK);
    }

    protected Task<HttpResponseMessage> PostPaymentAsync(
        Session s, Guid loanId, decimal amount, string? key = null, DateOnly? date = null,
        PaymentMethod method = PaymentMethod.Cash, string? token = null) =>
        SendAsync(HttpMethod.Post, $"/api/v1/loans/{loanId}/payments", token ?? s.Token, new CreatePaymentRequest
        {
            Amount = amount,
            PaymentDate = date ?? Today(),
            PaymentMethod = method,
            ReferenceNumber = "REF-1",
            Remarks = "EMI",
            IdempotencyKey = key ?? Guid.NewGuid().ToString("N")
        });

    protected async Task<PaymentResponse> PayAsync(
        Session s, Guid loanId, decimal amount, DateOnly? date = null, PaymentMethod method = PaymentMethod.Cash) =>
        await ReadAsync<PaymentResponse>(await PostPaymentAsync(s, loanId, amount, date: date, method: method), HttpStatusCode.Created);

    protected Task<HttpResponseMessage> PostReversalAsync(Session s, Guid paymentId, string? reason = "Cheque bounced", DateOnly? date = null, string? token = null) =>
        SendAsync(HttpMethod.Post, $"/api/v1/payments/{paymentId}/reversals", token ?? s.Token,
            new ReversePaymentRequest { Reason = reason ?? string.Empty, ReversalDate = date ?? Today() });

    protected async Task<PaymentResponse> ReverseAsync(Session s, Guid paymentId) =>
        await ReadAsync<PaymentResponse>(await PostReversalAsync(s, paymentId), HttpStatusCode.Created);

    protected async Task<PenaltyResponse> AssessAsync(Session s, Guid loanId, decimal amount, DateOnly? date = null, int? installment = null) =>
        await ReadAsync<PenaltyResponse>(
            await SendAsync(HttpMethod.Post, $"/api/v1/loans/{loanId}/penalties", s.Token,
                new AssessPenaltyRequest { Amount = amount, AssessedDate = date ?? Today(), InstallmentNumber = installment }),
            HttpStatusCode.Created);

    protected async Task<List<PenaltyResponse>> GetPenaltiesAsync(Session s, Guid loanId) =>
        await ReadAsync<List<PenaltyResponse>>(
            await SendAsync(HttpMethod.Get, $"/api/v1/loans/{loanId}/penalties", s.Token), HttpStatusCode.OK);

    protected Task<HttpResponseMessage> PostWaiverAsync(Session s, Guid loanId, Guid penaltyId, decimal amount, string? reason = "Goodwill", string? token = null) =>
        SendAsync(HttpMethod.Post, $"/api/v1/loans/{loanId}/penalties/{penaltyId}/waivers", token ?? s.Token,
            new WaivePenaltyRequest { Amount = amount, Reason = reason ?? string.Empty });

    protected Task<HttpResponseMessage> PostCloseAsync(Session s, Guid loanId) =>
        SendAsync(HttpMethod.Post, $"/api/v1/loans/{loanId}/close", s.Token, new CloseLoanRequest { ClosureDate = Today() });

    /// <summary>Everything still payable on the loan's current schedule: what a payment must equal to settle every installment.</summary>
    protected async Task<decimal> TotalRemainingAsync(Session s, Guid loanId) =>
        (await GetScheduleAsync(s, loanId)).Sum(r => r.RemainingAmount);
}
