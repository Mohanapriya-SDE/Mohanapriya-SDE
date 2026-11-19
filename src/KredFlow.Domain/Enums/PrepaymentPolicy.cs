namespace KredFlow.Domain.Enums;

/// <summary>Loan-level rule for what a prepayment reduces. LenderChoice means each prepayment names its own treatment.</summary>
public enum PrepaymentPolicy
{
    ReduceTenure,
    ReduceEmi,
    LenderChoice
}
