using KredFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KredFlow.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Remarks).HasMaxLength(500);
        builder.Property(x => x.ReferenceNumber).HasMaxLength(100);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100);
        builder.Property(x => x.RequestHash).HasMaxLength(64);

        builder.HasIndex(x => new { x.LoanId, x.PaymentDate });
        builder.HasIndex(x => new { x.LoanId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");

        builder.HasOne(x => x.Loan)
            .WithMany(x => x.Payments)
            .HasForeignKey(x => x.LoanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
