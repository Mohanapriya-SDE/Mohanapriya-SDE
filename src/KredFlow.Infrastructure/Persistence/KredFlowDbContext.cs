using KredFlow.Domain.Constants;
using KredFlow.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace KredFlow.Infrastructure.Persistence;

public class KredFlowDbContext(DbContextOptions<KredFlowDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    // Fixed ids keep the seeded roles stable across migrations.
    public static readonly Guid OwnerRoleId = new("5f1c2a64-8d0e-4b8e-9a53-0c6b7a1e2d01");
    public static readonly Guid StaffRoleId = new("5f1c2a64-8d0e-4b8e-9a53-0c6b7a1e2d02");

    public DbSet<Lender> Lenders => Set<Lender>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<RepaymentSchedule> RepaymentSchedules => Set<RepaymentSchedule>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<LoanTransaction> LoanTransactions => Set<LoanTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>().ToTable("Users");
        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("Roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");

        modelBuilder.Entity<IdentityRole<Guid>>().HasData(
            new IdentityRole<Guid> { Id = OwnerRoleId, Name = RoleNames.Owner, NormalizedName = RoleNames.Owner.ToUpperInvariant(), ConcurrencyStamp = "owner-role" },
            new IdentityRole<Guid> { Id = StaffRoleId, Name = RoleNames.Staff, NormalizedName = RoleNames.Staff.ToUpperInvariant(), ConcurrencyStamp = "staff-role" });

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KredFlowDbContext).Assembly);
    }
}
