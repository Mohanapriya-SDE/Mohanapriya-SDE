using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KredFlow.Application.Auth;
using KredFlow.Application.Common;
using KredFlow.Application.Customers;
using KredFlow.Domain.Entities;
using KredFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace KredFlow.Tests;

public sealed class CustomerApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "StrongPassword@123";
    private const string Base = "/api/v1/customers";

    private HttpClient Client { get; } = factory.CreateClient();

    private sealed record Session(Guid LenderId, string Token);

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

        var login = await Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return new Session(registered.LenderId, (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
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

    private async Task<CustomerResponse> CreateAsync(Session session, string name = "Meena Devi", string phone = "9123456789")
    {
        var response = await SendAsync(HttpMethod.Post, Base, session.Token,
            new CreateCustomerRequest { FullName = name, PhoneNumber = phone, Address = "Chennai" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CustomerResponse>())!;
    }

    [Fact]
    public async Task Create_ReturnsCreatedCustomerForCallersLender()
    {
        var session = await RegisterAndLoginAsync();

        var response = await SendAsync(HttpMethod.Post, Base, session.Token,
            new CreateCustomerRequest { FullName = "  Meena Devi  ", PhoneNumber = "9123456789", Address = "  Chennai  " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var customer = (await response.Content.ReadFromJsonAsync<CustomerResponse>())!;
        Assert.Equal($"{Base}/{customer.Id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal("Meena Devi", customer.FullName);
        Assert.Equal("+919123456789", customer.PhoneNumber);
        Assert.Equal("Chennai", customer.Address);
        Assert.True(customer.IsActive);

        var stored = await factory.WithDbAsync(db => db.Customers.SingleAsync(c => c.Id == customer.Id));
        Assert.Equal(session.LenderId, stored.LenderId);
    }

    [Theory]
    [InlineData("", "9123456789")]
    [InlineData("Meena", "12345")]
    [InlineData("Meena", "")]
    public async Task Create_InvalidInput_ReturnsBadRequest(string name, string phone)
    {
        var session = await RegisterAndLoginAsync();

        var response = await SendAsync(HttpMethod.Post, Base, session.Token,
            new CreateCustomerRequest { FullName = name, PhoneNumber = phone });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_AllowsDuplicatePhoneNumbers()
    {
        var session = await RegisterAndLoginAsync();

        await CreateAsync(session, "Meena Devi", "9123456789");
        var second = await CreateAsync(session, "Lakshmi Devi", "+919123456789");

        Assert.Equal("+919123456789", second.PhoneNumber);
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
            (HttpMethod.Put, $"{Base}/{id}"),
            (HttpMethod.Patch, $"{Base}/{id}/status"),
            (HttpMethod.Get, $"{Base}/{id}/loans"),
            (HttpMethod.Get, $"{Base}/{id}/outstanding")
        };

        foreach (var (method, url) in requests)
        {
            var response = await SendAsync(method, url, token: null, body: new { });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Get_ReturnsCustomer_AndUnknownIdReturnsNotFound()
    {
        var session = await RegisterAndLoginAsync();
        var created = await CreateAsync(session);

        var found = await SendAsync(HttpMethod.Get, $"{Base}/{created.Id}", session.Token);
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal(created.Id, (await found.Content.ReadFromJsonAsync<CustomerResponse>())!.Id);

        var missing = await SendAsync(HttpMethod.Get, $"{Base}/{Guid.NewGuid()}", session.Token);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task OtherLender_CannotReadOrModifyCustomer()
    {
        var owner = await RegisterAndLoginAsync();
        var intruder = await RegisterAndLoginAsync();
        var customer = await CreateAsync(owner);

        var update = new UpdateCustomerRequest { FullName = "Hacked", PhoneNumber = "9000000000" };
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"{Base}/{customer.Id}", intruder.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Put, $"{Base}/{customer.Id}", intruder.Token, update)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Patch, $"{Base}/{customer.Id}/status", intruder.Token, new UpdateCustomerStatusRequest { IsActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"{Base}/{customer.Id}/loans", intruder.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"{Base}/{customer.Id}/outstanding", intruder.Token)).StatusCode);

        var list = await SendAsync(HttpMethod.Get, Base, intruder.Token);
        Assert.Empty((await list.Content.ReadFromJsonAsync<PagedResponse<CustomerResponse>>())!.Items);

        var unchanged = await factory.WithDbAsync(db => db.Customers.SingleAsync(c => c.Id == customer.Id));
        Assert.Equal("Meena Devi", unchanged.FullName);
        Assert.True(unchanged.IsActive);
    }

    [Fact]
    public async Task Update_ReplacesEditableFields()
    {
        var session = await RegisterAndLoginAsync();
        var created = await CreateAsync(session);

        var response = await SendAsync(HttpMethod.Put, $"{Base}/{created.Id}", session.Token,
            new UpdateCustomerRequest { FullName = "Meena Sundaram", PhoneNumber = "+919000000001", Address = null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<CustomerResponse>())!;
        Assert.Equal("Meena Sundaram", updated.FullName);
        Assert.Equal("+919000000001", updated.PhoneNumber);
        Assert.Null(updated.Address);
    }

    [Fact]
    public async Task List_PaginatesAndSearchesWithinOwnLender()
    {
        var session = await RegisterAndLoginAsync();
        await CreateAsync(session, "Anita Rao", "9111111111");
        await CreateAsync(session, "Bala Murugan", "9222222222");
        var third = await CreateAsync(session, "Chitra Anand", "9333333333");
        await SendAsync(HttpMethod.Patch, $"{Base}/{third.Id}/status", session.Token, new UpdateCustomerStatusRequest { IsActive = false });

        var page1 = await GetPageAsync(session, "?page=1&pageSize=2");
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(["Anita Rao", "Bala Murugan"], page1.Items.Select(c => c.FullName));

        var page2 = await GetPageAsync(session, "?page=2&pageSize=2");
        Assert.Equal(["Chitra Anand"], page2.Items.Select(c => c.FullName));

        Assert.Equal(["Bala Murugan"], (await GetPageAsync(session, "?search=MURUG")).Items.Select(c => c.FullName));
        Assert.Equal(["Chitra Anand"], (await GetPageAsync(session, "?search=9333")).Items.Select(c => c.FullName));
        Assert.Equal(["Chitra Anand"], (await GetPageAsync(session, "?isActive=false")).Items.Select(c => c.FullName));
        Assert.Equal(2, (await GetPageAsync(session, "?isActive=true")).TotalCount);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    public async Task List_InvalidPaging_ReturnsBadRequest(string query)
    {
        var session = await RegisterAndLoginAsync();

        var response = await SendAsync(HttpMethod.Get, Base + query, session.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_DeactivatesAndReactivates_KeepingLoanHistory()
    {
        var session = await RegisterAndLoginAsync();
        var customer = await CreateAsync(session);
        var loanId = await AddLoanAsync(session.LenderId, customer.Id, LoanStatus.Active);

        var deactivate = await SendAsync(HttpMethod.Patch, $"{Base}/{customer.Id}/status", session.Token, new UpdateCustomerStatusRequest { IsActive = false });
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        Assert.False((await GetAsync(session, customer.Id)).IsActive);

        var loans = await GetLoansAsync(session, customer.Id);
        Assert.Equal(loanId, Assert.Single(loans).Id);

        var reactivate = await SendAsync(HttpMethod.Patch, $"{Base}/{customer.Id}/status", session.Token, new UpdateCustomerStatusRequest { IsActive = true });
        Assert.Equal(HttpStatusCode.NoContent, reactivate.StatusCode);
        Assert.True((await GetAsync(session, customer.Id)).IsActive);
    }

    [Fact]
    public async Task Loans_ReturnsNewestFirst()
    {
        var session = await RegisterAndLoginAsync();
        var customer = await CreateAsync(session);
        var older = await AddLoanAsync(session.LenderId, customer.Id, LoanStatus.Closed, new DateOnly(2025, 1, 1));
        var newer = await AddLoanAsync(session.LenderId, customer.Id, LoanStatus.Active, new DateOnly(2026, 1, 1));

        var loans = await GetLoansAsync(session, customer.Id);

        Assert.Equal([newer, older], loans.Select(l => l.Id));
        Assert.Equal(LoanStatus.Active, loans[0].Status);

        var raw = await (await SendAsync(HttpMethod.Get, $"{Base}/{customer.Id}/loans", session.Token)).Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"Active\"", raw);
    }

    [Fact]
    public async Task Outstanding_SumsUnpaidScheduleAmountsOfOpenLoansOnly()
    {
        var session = await RegisterAndLoginAsync();
        var customer = await CreateAsync(session);

        var scheduled = await AddLoanAsync(session.LenderId, customer.Id, LoanStatus.Active);
        await factory.WithDbAsync(async db =>
        {
            var first = new RepaymentSchedule { Id = Guid.NewGuid(), LoanId = scheduled, InstallmentNumber = 1, DueDate = new DateOnly(2026, 1, 1), PrincipalDue = 800m, InterestDue = 100m, TotalDue = 900m };
            var second = new RepaymentSchedule { Id = Guid.NewGuid(), LoanId = scheduled, InstallmentNumber = 2, DueDate = new DateOnly(2026, 2, 1), PrincipalDue = 810m, InterestDue = 90m, TotalDue = 900m };
            var payment = new Payment { Id = Guid.NewGuid(), LoanId = scheduled, Amount = 900m, PaymentDate = new DateOnly(2026, 1, 1), PaymentMethod = PaymentMethod.Cash };
            db.RepaymentSchedules.AddRange(first, second);
            db.Payments.Add(payment);
            db.PaymentAllocations.Add(new PaymentAllocation { Id = Guid.NewGuid(), PaymentId = payment.Id, ScheduleId = first.Id, InterestAmount = 100m, PrincipalAmount = 800m });
            return await db.SaveChangesAsync();
        });
        await AddLoanAsync(session.LenderId, customer.Id, LoanStatus.Overdue); // no schedule yet: owes its principal (10,000)
        await AddLoanAsync(session.LenderId, customer.Id, LoanStatus.Closed);

        var response = await SendAsync(HttpMethod.Get, $"{Base}/{customer.Id}/outstanding", session.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var outstanding = (await response.Content.ReadFromJsonAsync<CustomerOutstandingResponse>())!;
        Assert.Equal(customer.Id, outstanding.CustomerId);
        Assert.Equal(2, outstanding.OpenLoanCount);
        Assert.Equal(10_900m, outstanding.TotalOutstanding);
    }

    [Fact]
    public async Task Outstanding_WithNoLoans_IsZero()
    {
        var session = await RegisterAndLoginAsync();
        var customer = await CreateAsync(session);

        var response = await SendAsync(HttpMethod.Get, $"{Base}/{customer.Id}/outstanding", session.Token);

        var outstanding = (await response.Content.ReadFromJsonAsync<CustomerOutstandingResponse>())!;
        Assert.Equal(0, outstanding.OpenLoanCount);
        Assert.Equal(0m, outstanding.TotalOutstanding);
    }

    private async Task<PagedResponse<CustomerResponse>> GetPageAsync(Session session, string query)
    {
        var response = await SendAsync(HttpMethod.Get, Base + query, session.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResponse<CustomerResponse>>())!;
    }

    private async Task<CustomerResponse> GetAsync(Session session, Guid id) =>
        (await (await SendAsync(HttpMethod.Get, $"{Base}/{id}", session.Token)).Content.ReadFromJsonAsync<CustomerResponse>())!;

    private async Task<List<CustomerLoanResponse>> GetLoansAsync(Session session, Guid id)
    {
        var response = await SendAsync(HttpMethod.Get, $"{Base}/{id}/loans", session.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<CustomerLoanResponse>>())!;
    }

    private async Task<Guid> AddLoanAsync(Guid lenderId, Guid customerId, LoanStatus status, DateOnly? startDate = null)
    {
        var loanId = Guid.NewGuid();
        await factory.WithDbAsync(async db =>
        {
            db.Loans.Add(new Loan
            {
                Id = loanId,
                LenderId = lenderId,
                CustomerId = customerId,
                Principal = 10_000m,
                AnnualInterestRate = 12m,
                TenureMonths = 12,
                EmiAmount = 900m,
                StartDate = startDate ?? new DateOnly(2026, 1, 1),
                Status = status
            });
            return await db.SaveChangesAsync();
        });
        return loanId;
    }
}
