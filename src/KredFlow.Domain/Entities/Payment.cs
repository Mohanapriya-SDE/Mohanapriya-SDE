using KredFlow.Domain.Enums;

namespace KredFlow.Domain.Entities;

public class Payment
{
    public Guid Id { get; set; }
    public Guid LoanId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly PaymentDate { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentType Type { get; set; } = PaymentType.Regular;
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }

    public Loan Loan { get; set; } = null!;
    public ICollection<PaymentAllocation> Allocations { get; set; } = new List<PaymentAllocation>();
}
