using KredFlow.Domain.Enums;

namespace KredFlow.Domain.Entities;

/// <summary>What a prepayment did to the loan, kept so that reversing the prepayment can restore the previous terms.</summary>
public class LoanRecast
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }
    public PrepaymentTreatment Treatment { get; set; }
    public decimal PreviousEmiAmount { get; set; }
    public int PreviousTenureMonths { get; set; }
    public decimal NewEmiAmount { get; set; }
    public int NewTenureMonths { get; set; }
    public decimal PrincipalOutstandingAfter { get; set; }

    public Payment Payment { get; set; } = null!;
}
