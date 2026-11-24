using KredFlow.Domain.Enums;

namespace KredFlow.Domain.Entities;

public class LoanTransaction
{
    public Guid Id { get; set; }
    public Guid LoanId { get; set; }
    public TransactionType TransactionType { get; set; }
    public decimal Amount { get; set; }
    public DateOnly TransactionDate { get; set; }

    /// <summary>The payment, penalty or waiver this entry belongs to. Null for entries created before references existed.</summary>
    public Guid? ReferenceId { get; set; }

    public Loan Loan { get; set; } = null!;
}
