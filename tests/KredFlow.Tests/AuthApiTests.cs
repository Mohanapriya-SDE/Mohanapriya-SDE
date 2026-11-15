using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KredFlow.Application.Auth;
using KredFlow.Domain.Constants;
using KredFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace KredFlow.Tests;

public sealed class AuthApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "StrongPassword@123";

    private HttpClient Client { get; } = factory.CreateClient();

    private static RegisterLenderRequest NewRegistration(string? email = null, string? password = null) => new()
    {
        BusinessName = "ABC Finance",
        OwnerName = "Ravi Kumar",
        Email = email ?? $"{Guid.NewGuid():N}@example.com",
        PhoneNumber = "9876543210",
        Password = password ?? Password
    };

    private async Task<RegisterLenderResponse> RegisterAsync(RegisterLenderRequest? request = null)
    {
        var response = await Client.PostAsJsonAsync("/api/v1/auth/register", request ?? NewRegistration());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RegisterLenderResponse>())!;
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });

    private async Task<LoginResponse> LoginOkAsync(string email)
    {
        var response = await LoginAsync(email, Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    // The traceId differs per request, so failures are compared on their stable fields only.
    private static async Task<(int Status, string? Title, string? Detail)> ReadProblemAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (
            json.GetProperty("status").GetInt32(),
            json.GetProperty("title").GetString(),
            json.TryGetProperty("detail", out var detail) ? detail.GetString() : null);
    }

    [Fact]
    public async Task Register_Valid_Returns201AndPersistsLenderUserAndOwnerRole()
    {
        var request = NewRegistration(email: $"  {Guid.NewGuid():N}@Example.com ");

        var registered = await RegisterAsync(request);

        Assert.Equal("Registration successful", registered.Message);
        Assert.Equal(request.Email.Trim().ToLowerInvariant(), registered.Email);
        await factory.WithDbAsync(async db =>
        {
            var lender = await db.Lenders.SingleAsync(l => l.Id == registered.LenderId);
            var user = await db.Set<ApplicationUser>().SingleAsync(u => u.Id == registered.UserId);

            Assert.Equal("+919876543210", lender.PhoneNumber);
            Assert.Equal(Lender.DefaultTimeZone, lender.TimeZone);
            Assert.True(lender.IsActive);
            Assert.Equal(lender.Id, user.LenderId);
            Assert.NotEqual(Password, user.PasswordHash);
            var roleNames = await (from ur in db.UserRoles
                                   join r in db.Roles on ur.RoleId equals r.Id
                                   where ur.UserId == user.Id
                                   select r.Name).ToListAsync();
            Assert.Equal([RoleNames.Owner], roleNames);
            return 0;
        });
    }

    [Fact]
    public async Task Register_DuplicateEmailInDifferentCase_Returns409AndCreatesNoSecondLender()
    {
        var first = NewRegistration();
        await RegisterAsync(first);
        var lenderCount = await factory.WithDbAsync(db => db.Lenders.CountAsync());

        first.Email = first.Email.ToUpperInvariant();
        var response = await Client.PostAsJsonAsync("/api/v1/auth/register", first);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(lenderCount, await factory.WithDbAsync(db => db.Lenders.CountAsync()));
    }

    [Fact]
    public async Task Register_WeakPasswordAndBadFields_Returns400WithFieldErrors()
    {
        var request = NewRegistration(password: "short");
        request.BusinessName = string.Empty;
        request.PhoneNumber = "12345";

        var response = await Client.PostAsJsonAsync("/api/v1/auth/register", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("password", out _));
        Assert.True(errors.TryGetProperty("businessName", out _));
        Assert.True(errors.TryGetProperty("phoneNumber", out _));
    }

    [Fact]
    public async Task Login_Valid_ReturnsTokenWithExpectedClaims()
    {
        var registered = await RegisterAsync();

        var login = await LoginOkAsync(registered.Email);

        Assert.Equal("Bearer", login.TokenType);
        Assert.Equal(900, login.ExpiresIn);
        Assert.Equal(RoleNames.Owner, login.User.Role);
        Assert.Equal(registered.LenderId, login.User.LenderId);
        Assert.Equal("ABC Finance", login.User.BusinessName);

        var token = new JsonWebTokenHandler().ReadJsonWebToken(login.AccessToken);
        Assert.Equal(registered.UserId.ToString(), token.Subject);
        Assert.Equal(registered.Email, token.GetClaim("email").Value);
        Assert.Equal(registered.LenderId.ToString(), token.GetClaim("lenderId").Value);
        Assert.Equal(RoleNames.Owner, token.GetClaim("role").Value);
        Assert.False(string.IsNullOrEmpty(token.Id));
        Assert.Equal(TimeSpan.FromMinutes(15), token.ValidTo - token.ValidFrom);
    }

    [Fact]
    public async Task Login_EveryFailureReason_ReturnsIdentical401()
    {
        var wrongPassword = await RegisterAsync();
        var inactiveUser = await RegisterAsync();
        var inactiveLender = await RegisterAsync();
        await factory.SetUserActiveAsync(inactiveUser.UserId, false);
        await factory.SetLenderActiveAsync(inactiveLender.LenderId, false);

        var responses = new[]
        {
            await LoginAsync(wrongPassword.Email, "WrongPassword@123"),
            await LoginAsync("nobody@example.com", Password),
            await LoginAsync(inactiveUser.Email, Password),
            await LoginAsync(inactiveLender.Email, Password)
        };

        var problems = new List<(int, string?, string?)>();
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            problems.Add(await ReadProblemAsync(response));
        }

        Assert.Single(problems.Distinct());
    }

    [Fact]
    public async Task Login_AfterRepeatedFailures_LocksAccountEvenForCorrectPassword()
    {
        var registered = await RegisterAsync();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(registered.Email, "WrongPassword@123")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(registered.Email, Password)).StatusCode);
    }

    [Fact]
    public async Task Login_InvalidEmailFormat_Returns400()
    {
        var response = await LoginAsync("not-an-email", Password);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithToken_ReturnsCurrentUser()
    {
        var registered = await RegisterAsync();
        var login = await LoginOkAsync(registered.Email);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = (await response.Content.ReadFromJsonAsync<UserInfoResponse>())!;
        Assert.Equal(registered.UserId, me.UserId);
        Assert.Equal(registered.LenderId, me.LenderId);
        Assert.Equal("Ravi Kumar", me.FullName);
        Assert.Equal(RoleNames.Owner, me.Role);
    }

    [Fact]
    public async Task Me_WithoutOrWithInvalidToken_Returns401ProblemDetails()
    {
        var anonymous = await Client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("application/problem+json", anonymous.Content.Headers.ContentType?.MediaType);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.jwt");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Me_TokenIssuedBeforeLenderDeactivated_Returns401()
    {
        var registered = await RegisterAsync();
        var login = await LoginOkAsync(registered.Email);
        await factory.SetLenderActiveAsync(registered.LenderId, false);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.SendAsync(request)).StatusCode);
    }
}
