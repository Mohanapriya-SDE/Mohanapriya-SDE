using KredFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KredFlow.Infrastructure.Persistence.Configurations;

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(x => x.FullName).IsRequired().HasMaxLength(150);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        // Identity's own EmailIndex is non-unique; email is the login so it must be.
        builder.HasIndex(x => x.NormalizedEmail).IsUnique().HasDatabaseName("UX_Users_NormalizedEmail");

        builder.HasOne(x => x.Lender)
            .WithMany(x => x.Users)
            .HasForeignKey(x => x.LenderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
