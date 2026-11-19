using KredFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KredFlow.Infrastructure.Persistence.Configurations;

public class PaymentAllocationConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> builder)
    {
        builder.ToTable("PaymentAllocations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.InterestAmount).HasPrecision(18, 2);
        builder.Property(x => x.PrincipalAmount).HasPrecision(18, 2);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);

        builder.HasOne(x => x.Payment)
            .WithMany(x => x.Allocations)
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Schedule)
            .WithMany(x => x.Allocations)
            .HasForeignKey(x => x.ScheduleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
