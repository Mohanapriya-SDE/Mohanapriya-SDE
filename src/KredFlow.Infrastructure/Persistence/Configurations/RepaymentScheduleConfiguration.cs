using KredFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KredFlow.Infrastructure.Persistence.Configurations;

public class RepaymentScheduleConfiguration : IEntityTypeConfiguration<RepaymentSchedule>
{
    public void Configure(EntityTypeBuilder<RepaymentSchedule> builder)
    {
        builder.ToTable("RepaymentSchedules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PrincipalDue).HasPrecision(18, 2);
        builder.Property(x => x.InterestDue).HasPrecision(18, 2);
        builder.Property(x => x.TotalDue).HasPrecision(18, 2);
        builder.Property(x => x.OpeningPrincipal).HasPrecision(18, 2);

        // A recast supersedes rows and reuses their numbers, so uniqueness only applies to current rows.
        builder.HasIndex(x => new { x.LoanId, x.InstallmentNumber })
            .IsUnique()
            .HasFilter("\"IsSuperseded\" = false");

        builder.HasIndex(x => x.SupersededByPaymentId);
        builder.HasIndex(x => x.CreatedByPaymentId);

        builder.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(x => x.CreatedByPaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(x => x.SupersededByPaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Loan)
            .WithMany(x => x.RepaymentSchedules)
            .HasForeignKey(x => x.LoanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
