using KredFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KredFlow.Infrastructure.Persistence.Configurations;

public class PenaltyConfiguration : IEntityTypeConfiguration<Penalty>
{
    public void Configure(EntityTypeBuilder<Penalty> builder)
    {
        builder.ToTable("Penalties");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.HasIndex(x => x.LoanId);

        builder.HasOne(x => x.Loan)
            .WithMany(x => x.Penalties)
            .HasForeignKey(x => x.LoanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<RepaymentSchedule>()
            .WithMany()
            .HasForeignKey(x => x.ScheduleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class PenaltyWaiverConfiguration : IEntityTypeConfiguration<PenaltyWaiver>
{
    public void Configure(EntityTypeBuilder<PenaltyWaiver> builder)
    {
        builder.ToTable("PenaltyWaivers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(500);
        builder.HasIndex(x => x.PenaltyId);

        builder.HasOne(x => x.Penalty)
            .WithMany(x => x.Waivers)
            .HasForeignKey(x => x.PenaltyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PaymentReversalConfiguration : IEntityTypeConfiguration<PaymentReversal>
{
    public void Configure(EntityTypeBuilder<PaymentReversal> builder)
    {
        builder.ToTable("PaymentReversals");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(500);

        // One reversal per payment: this is what stops a payment being reversed twice.
        builder.HasIndex(x => x.PaymentId).IsUnique();

        builder.HasOne(x => x.Payment)
            .WithOne(x => x.Reversal)
            .HasForeignKey<PaymentReversal>(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class LoanRecastConfiguration : IEntityTypeConfiguration<LoanRecast>
{
    public void Configure(EntityTypeBuilder<LoanRecast> builder)
    {
        builder.ToTable("LoanRecasts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Treatment).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.PreviousEmiAmount).HasPrecision(18, 2);
        builder.Property(x => x.NewEmiAmount).HasPrecision(18, 2);
        builder.Property(x => x.PrincipalOutstandingAfter).HasPrecision(18, 2);
        builder.HasIndex(x => x.PaymentId).IsUnique();

        builder.HasOne(x => x.Payment)
            .WithOne(x => x.Recast)
            .HasForeignKey<LoanRecast>(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
