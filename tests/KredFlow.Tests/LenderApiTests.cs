using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KredFlow.Application.Auth;
using KredFlow.Application.Lenders;
using KredFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace KredFlow.Tests;

public sealed class LenderApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "StrongPassword@123";

    private HttpClient Client { get; } = factory.CreateClient();

    private sealed record Session(Guid LenderId, string Email, string Token);

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

    private static UpdateLenderRequest ValidUpdate() => new()
    {
        BusinessName = "  Sri Lakshmi Finance  ",
        PhoneNumber = "9123456789",
        Address = "Chennai",
        TimeZone = "Asia/Kolkata"
    };

    [Fact]
    public async Task GetMe_ReturnsRegisteredLender()
    {
        var session = await RegisterAndLoginAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/lenders/me", session.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lender = (await response.Content.ReadFromJsonAsync<LenderResponse>())!;
        Assert.Equal(session.LenderId, lender.Id);
        Assert.Equal("ABC Finance", lender.BusinessName);
        Assert.Equal("Ravi Kumar", lender.OwnerName);
        Assert.Equal(session.Email, lender.Email);
        Assert.Equal("+919876543210", lender.PhoneNumber);
        Assert.Equal("Asia/Kolkata", lender.TimeZone);
        Assert.True(lender.IsActive);
        Assert.Equal(DateTimeKind.Utc, lender.CreatedAt.Kind);
    }

    [Fact]
    public async Task GetMe_WithoutToken_Returns401()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/lenders/me", token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PutMe_UpdatesProfileAndNormalizesPhone_ButNotEmailOrOwnerName()
    {
        var session = await RegisterAndLoginAsync();

        var response = await SendAsync(HttpMethod.Put, "/api/v1/lenders/me", session.Token, ValidUpdate());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<LenderResponse>())!;
        Assert.Equal("Sri Lakshmi Finance", updated.BusinessName);
        Assert.Equal("+919123456789", updated.PhoneNumber);
        Assert.Equal("Chennai", updated.Address);
        Assert.Equal(session.Email, updated.Email);
        Assert.Equal("Ravi Kumar", updated.OwnerName);

        var reread = (await (await SendAsync(HttpMethod.Get, "/api/v1/lenders/me", session.Token))
            .Content.ReadFromJsonAsync<LenderResponse>())!;
        Assert.Equal("Sri Lakshmi Finance", reread.BusinessName);
    }

    [Fact]
    public async Task PutMe_InvalidFields_Returns400WithFieldErrors()
    {
        var session = await RegisterAndLoginAsync();
        var request = ValidUpdate();
        request.BusinessName = string.Empty;
        request.PhoneNumber = "12345";
        request.TimeZone = "Europe/London";

        var response = await SendAsync(HttpMethod.Put, "/api/v1/lenders/me", session.Token, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("businessName", out _));
        Assert.True(errors.TryGetProperty("phoneNumber", out _));
        Assert.True(errors.TryGetProperty("timeZone", out _));
    }

    [Fact]
    public async Task PutAndPatch_AsStaff_Return403()
    {
        var owner = await RegisterAndLoginAsync();
        var staffEmail = $"{Guid.NewGuid():N}@example.com";
        await factory.AddStaffUserAsync(owner.LenderId, staffEmail, Password);
        var staffToken = await LoginAsync(staffEmail);

        var put = await SendAsync(HttpMethod.Put, "/api/v1/lenders/me", staffToken, ValidUpdate());
        var patch = await SendAsync(HttpMethod.Patch, "/api/v1/lenders/me/status", staffToken, new UpdateLenderStatusRequest { IsActive = false });
        var get = await SendAsync(HttpMethod.Get, "/api/v1/lenders/me", staffToken);

        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }

    [Fact]
    public async Task PatchStatus_Deactivate_Returns204_BlocksAccessAndKeepsHistoricalData()
    {
        var session = await RegisterAndLoginAsync();
        await factory.AddLoanAsync(session.LenderId, LoanStatus.Closed);
        await factory.AddLoanAsync(session.LenderId, LoanStatus.Defaulted);

        var response = await SendAsync(HttpMethod.Patch, "/api/v1/lenders/me/status", session.Token, new UpdateLenderStatusRequest { IsActive = false });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/v1/lenders/me", session.Token)).StatusCode);
        var login = await Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = session.Email, Password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        await factory.WithDbAsync(async db =>
        {
            Assert.False((await db.Lenders.SingleAsync(l => l.Id == session.LenderId)).IsActive);
            Assert.Equal(2, await db.Loans.CountAsync(l => l.LenderId == session.LenderId));
            Assert.Equal(2, await db.Customers.CountAsync(c => c.LenderId == session.LenderId));
            return 0;
        });
    }

    [Fact]
    public async Task PatchStatus_Reactivate_Returns422()
    {
        var session = await RegisterAndLoginAsync();

        var response = await SendAsync(HttpMethod.Patch, "/api/v1/lenders/me/status", session.Token, new UpdateLenderStatusRequest { IsActive = true });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(LoanStatus.Active)]
    [InlineData(LoanStatus.Overdue)]
    public async Task PatchStatus_WithOpenLoan_Returns422_AndLenderStaysActive(LoanStatus openStatus)
    {
        var session = await RegisterAndLoginAsync();
        await factory.AddLoanAsync(session.LenderId, openStatus);

        var response = await SendAsync(HttpMethod.Patch, "/api/v1/lenders/me/status", session.Token, new UpdateLenderStatusRequest { IsActive = false });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/v1/lenders/me", session.Token)).StatusCode);
    }
}
