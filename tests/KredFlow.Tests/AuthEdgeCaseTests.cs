using System.Net;
using System.Net.Http.Json;
using KredFlow.Application.Auth;
using KredFlow.Domain.Entities;
using KredFlow.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KredFlow.Tests;

public sealed class AuthEdgeCaseTests
{
    [Fact]
    public async Task Login_ExceedingRateLimit_Returns429WithRetryAfter()
    {
        await using var factory = new LowLoginLimitApiFactory();
        using var client = factory.CreateClient();
        var body = new LoginRequest { Email = "nobody@example.com", Password = "WrongPassword@123" };

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", body)).StatusCode);
        var limited = await client.PostAsJsonAsync("/api/v1/auth/login", body);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void Startup_WithoutSigningKey_Fails()
    {
        using var factory = new MissingSigningKeyApiFactory();

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public async Task ExecuteInTransaction_WhenActionThrows_RollsBackEverything()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = new KredFlowDbContext(new DbContextOptionsBuilder<KredFlowDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new UnitOfWork(db).ExecuteInTransactionAsync(async ct =>
        {
            db.Lenders.Add(new Lender { Id = Guid.NewGuid(), BusinessName = "X", OwnerName = "Y", Email = "x@example.com", PhoneNumber = "+919876543210" });
            await db.SaveChangesAsync(ct);
            throw new InvalidOperationException("boom");
        }));

        Assert.Equal(0, await db.Lenders.AsNoTracking().CountAsync());
    }

    [Theory]
    [InlineData("9876543210", "+919876543210")]
    [InlineData("+919876543210", "+919876543210")]
    [InlineData(" 6123456789 ", "+916123456789")]
    public void PhoneNumber_Valid_IsNormalized(string input, string expected)
    {
        Assert.True(PhoneNumberFormat.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("5876543210")]
    [InlineData("+449876543210")]
    [InlineData("98765432101")]
    public void PhoneNumber_Invalid_IsRejected(string input) =>
        Assert.False(PhoneNumberFormat.TryNormalize(input, out _));

    [Theory]
    [InlineData("Short1!", false)]
    [InlineData("alllowercase123!", false)]
    [InlineData("ALLUPPERCASE123!", false)]
    [InlineData("NoDigitsHere!!!!", false)]
    [InlineData("NoSymbolsHere1234", false)]
    [InlineData("StrongPassword@123", true)]
    public void RegisterValidator_EnforcesPasswordPolicy(string password, bool valid)
    {
        var request = new RegisterLenderRequest
        {
            BusinessName = "ABC Finance",
            OwnerName = "Ravi Kumar",
            Email = "ravi@example.com",
            PhoneNumber = "9876543210",
            Password = password
        };

        Assert.Equal(valid, new RegisterLenderRequestValidator().Validate(request).IsValid);
    }
}
