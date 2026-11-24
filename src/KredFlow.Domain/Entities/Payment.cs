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
    public string? ReferenceNumber { get; set; }

    /// <summary>Client-supplied key that makes retries of the same request safe. Unique per loan when present.</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Fingerprint of the request that used the key, to detect the key being reused for a different request.</summary>
    public string? RequestHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public Loan Loan { get; set; } = null!;
    public PaymentReversal? Reversal { get; set; }
    public LoanRecast? Recast { get; set; }
    public ICollection<PaymentAllocation> Allocations { get; set; } = new List<PaymentAllocation>();
}
