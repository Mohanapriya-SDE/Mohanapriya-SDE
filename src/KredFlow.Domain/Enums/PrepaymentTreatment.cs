namespace KredFlow.Domain.Enums;

/// <summary>What a single prepayment reduces: the number of remaining installments or the EMI amount.</summary>
public enum PrepaymentTreatment
{
    ReduceTenure,
    ReduceEmi
}
