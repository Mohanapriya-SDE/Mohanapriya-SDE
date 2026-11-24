using KredFlow.Domain.Enums;

namespace KredFlow.Domain.Entities;

public class PaymentAllocation
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }
    public Guid? ScheduleId { get; set; }
    public Guid? PenaltyId { get; set; }
    public AllocationType Type { get; set; } = AllocationType.Installment;
    public decimal InterestAmount { get; set; }
    public decimal PrincipalAmount { get; set; }
    public decimal PenaltyAmount { get; set; }

    public Payment Payment { get; set; } = null!;
    public RepaymentSchedule? Schedule { get; set; }
    public Penalty? Penalty { get; set; }
}
