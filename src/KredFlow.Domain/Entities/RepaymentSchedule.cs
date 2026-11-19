namespace KredFlow.Domain.Entities;

public class RepaymentSchedule
{
    public Guid Id { get; set; }
    public Guid LoanId { get; set; }
    public int InstallmentNumber { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal OpeningPrincipal { get; set; }
    public decimal PrincipalDue { get; set; }
    public decimal InterestDue { get; set; }
    public decimal TotalDue { get; set; }

    /// <summary>Set when a prepayment recast replaced this row. Rows with allocations are never deleted.</summary>
    public bool IsSuperseded { get; set; }

    public Loan Loan { get; set; } = null!;
    public ICollection<PaymentAllocation> Allocations { get; set; } = new List<PaymentAllocation>();
}
