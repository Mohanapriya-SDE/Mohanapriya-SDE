using KredFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KredFlow.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FullName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.PhoneNumber).IsRequired().HasMaxLength(20);
        builder.Property(x => x.Address).HasMaxLength(500);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        // Phone numbers are intentionally not unique: family members can share one.
        builder.HasIndex(x => new { x.LenderId, x.PhoneNumber });
        builder.HasIndex(x => new { x.LenderId, x.FullName });

        builder.HasOne(x => x.Lender)
            .WithMany(x => x.Customers)
            .HasForeignKey(x => x.LenderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
