namespace KredFlow.Domain.Entities;

public class PenaltyWaiver
{
    public Guid Id { get; set; }
    public Guid PenaltyId { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public Penalty Penalty { get; set; } = null!;
}
