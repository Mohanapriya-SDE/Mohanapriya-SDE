namespace KredFlow.Domain.Entities;

/// <summary>A payment is never deleted. Once it has a reversal, its allocations stop counting toward any balance.</summary>
public class PaymentReversal
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateOnly ReversalDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public Payment Payment { get; set; } = null!;
}
