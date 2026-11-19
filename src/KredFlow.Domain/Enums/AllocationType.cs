namespace KredFlow.Domain.Enums;

public enum AllocationType
{
    /// <summary>Applied to a repayment schedule row.</summary>
    Installment,

    /// <summary>Principal paid ahead of the schedule; it belongs to no row.</summary>
    PrincipalPrepayment
}
