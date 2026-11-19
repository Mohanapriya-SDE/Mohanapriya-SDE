using KredFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KredFlow.Infrastructure.Persistence.Configurations;

public class LoanConfiguration : IEntityTypeConfiguration<Loan>
{
    public void Configure(EntityTypeBuilder<Loan> builder)
    {
        builder.ToTable("Loans");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Principal).HasPrecision(18, 2);
        builder.Property(x => x.AnnualInterestRate).HasPrecision(5, 2);
        builder.Property(x => x.EmiAmount).HasPrecision(18, 2);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.PrepaymentPolicy).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.ClosureRemarks).HasMaxLength(500);
        builder.Property(x => x.Version).IsConcurrencyToken();

        builder.HasIndex(x => new { x.LenderId, x.CustomerId });
        builder.HasIndex(x => new { x.LenderId, x.Status, x.StartDate });

        builder.HasOne(x => x.Lender)
            .WithMany(x => x.Loans)
            .HasForeignKey(x => x.LenderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Customer)
            .WithMany(x => x.Loans)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
