using KredFlow.Domain.Entities;
using KredFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KredFlow.Tests;

/// <summary>Hosts the API against an in-memory Sqlite database; "Testing" skips the Development migrate/seed block.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string SigningKey = "test-signing-key-test-signing-key-0123456789";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected virtual Dictionary<string, string?> Settings => new()
    {
        ["Jwt:SigningKey"] = SigningKey,
        ["RateLimiting:Login:PermitLimit"] = "1000",
        ["RateLimiting:Register:PermitLimit"] = "1000"
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Settings));
        builder.ConfigureTestServices(services =>
        {
            foreach (var descriptor in services
                         .Where(d => d.ServiceType == typeof(KredFlowDbContext)
                                     || d.ServiceType.GenericTypeArguments.Contains(typeof(KredFlowDbContext)))
                         .ToList())
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<KredFlowDbContext>(options => options.UseSqlite(_connection));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<KredFlowDbContext>().Database.EnsureCreated();
        return host;
    }

    public async Task<T> WithDbAsync<T>(Func<KredFlowDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<KredFlowDbContext>());
    }

    public async Task SetLenderActiveAsync(Guid lenderId, bool isActive) =>
        await WithDbAsync(async db =>
        {
            var lender = await db.Lenders.SingleAsync(l => l.Id == lenderId);
            lender.IsActive = isActive;
            return await db.SaveChangesAsync();
        });

    public async Task SetUserActiveAsync(Guid userId, bool isActive) =>
        await WithDbAsync(async db =>
        {
            var user = await db.Set<ApplicationUser>().SingleAsync(u => u.Id == userId);
            user.IsActive = isActive;
            return await db.SaveChangesAsync();
        });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}

public sealed class LowLoginLimitApiFactory : ApiFactory
{
    protected override Dictionary<string, string?> Settings
    {
        get
        {
            var settings = base.Settings;
            settings["RateLimiting:Login:PermitLimit"] = "2";
            return settings;
        }
    }
}

public sealed class MissingSigningKeyApiFactory : ApiFactory
{
    protected override Dictionary<string, string?> Settings
    {
        get
        {
            var settings = base.Settings;
            settings.Remove("Jwt:SigningKey");
            return settings;
        }
    }
}
