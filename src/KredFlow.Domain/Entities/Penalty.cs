namespace KredFlow.Domain.Entities;

/// <summary>A charge on a loan. Paid, waived and outstanding amounts are derived from allocations and waivers, never stored.</summary>
public class Penalty
{
    public Guid Id { get; set; }
    public Guid LoanId { get; set; }
    public Guid? ScheduleId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly AssessedDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public Loan Loan { get; set; } = null!;
    public ICollection<PenaltyWaiver> Waivers { get; set; } = new List<PenaltyWaiver>();
}
