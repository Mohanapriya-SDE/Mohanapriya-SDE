using KredFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KredFlow.Infrastructure.Persistence;

public class KredFlowDbContext(DbContextOptions<KredFlowDbContext> options) : DbContext(options)
{
    public DbSet<Lender> Lenders => Set<Lender>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<RepaymentSchedule> RepaymentSchedules => Set<RepaymentSchedule>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<LoanTransaction> LoanTransactions => Set<LoanTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KredFlowDbContext).Assembly);
    }
}
