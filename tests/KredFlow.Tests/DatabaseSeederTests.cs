using Bogus;
using KredFlow.Domain.Enums;
using KredFlow.Infrastructure.Persistence;
using KredFlow.Infrastructure.Persistence.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KredFlow.Tests;

public sealed class DatabaseSeederTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly KredFlowDbContext _db;

    public DatabaseSeederTests()
    {
        _connection.Open();
        _db = new KredFlowDbContext(new DbContextOptionsBuilder<KredFlowDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task SeedAsync_InsertsExpectedRecords()
    {
        await new DatabaseSeeder(_db).SeedAsync();

        Assert.Equal(2, await _db.Lenders.CountAsync());
        Assert.Equal(10, await _db.Customers.CountAsync());
        Assert.Equal(15, await _db.Loans.CountAsync());
        Assert.Equal(await _db.Loans.SumAsync(l => l.TenureMonths), await _db.RepaymentSchedules.CountAsync());
        Assert.Equal(await _db.Payments.CountAsync(), await _db.PaymentAllocations.CountAsync());
        Assert.NotEmpty(await _db.LoanTransactions.ToListAsync());
    }

    [Fact]
    public async Task SeedAsync_CoversAllLoanStatuses()
    {
        await new DatabaseSeeder(_db).SeedAsync();

        var statuses = (await _db.Loans.Select(l => l.Status).Distinct().ToListAsync()).ToHashSet();
        Assert.Equal(Enum.GetValues<LoanStatus>().ToHashSet(), statuses);
    }

    [Fact]
    public async Task SeedAsync_CustomerAndLoanShareSameLender()
    {
        await new DatabaseSeeder(_db).SeedAsync();

        Assert.True(await _db.Loans.AllAsync(l => l.LenderId == l.Customer.LenderId));
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        var seeder = new DatabaseSeeder(_db);
        await seeder.SeedAsync();
        await seeder.SeedAsync();

        Assert.Equal(15, await _db.Loans.CountAsync());
    }

    [Fact]
    public void Faker_WithSameSeed_ProducesSameNames()
    {
        // Guards the determinism assumption the seeder relies on.
        var a = new Faker("en_IND") { Random = new Randomizer(1) }.Name.FullName();
        var b = new Faker("en_IND") { Random = new Randomizer(1) }.Name.FullName();

        Assert.Equal(a, b);
    }
}
