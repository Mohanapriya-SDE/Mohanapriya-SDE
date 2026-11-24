using KredFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KredFlow.Infrastructure.Persistence.Configurations;

public class LoanTransactionConfiguration : IEntityTypeConfiguration<LoanTransaction>
{
    public void Configure(EntityTypeBuilder<LoanTransaction> builder)
    {
        builder.ToTable("LoanTransactions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.TransactionType).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(x => x.ReferenceId);

        builder.HasOne(x => x.Loan)
            .WithMany(x => x.Transactions)
            .HasForeignKey(x => x.LoanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
