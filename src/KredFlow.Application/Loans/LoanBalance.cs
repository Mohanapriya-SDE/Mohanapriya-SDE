using KredFlow.Domain.Entities;

namespace KredFlow.Application.Loans;

/// <summary>A schedule row together with what has been allocated to it. Paid and remaining are always derived from allocations.</summary>
public sealed record ScheduleRowFigures(
    Guid Id,
    int InstallmentNumber,
    DateOnly DueDate,
    decimal OpeningPrincipal,
    decimal PrincipalDue,
    decimal InterestDue,
    decimal PaidPrincipal,
    decimal PaidInterest)
{
    public decimal TotalDue => PrincipalDue + InterestDue;
    public decimal Paid => PaidPrincipal + PaidInterest;
    public decimal Remaining => Math.Max(0m, TotalDue - Paid);
    public decimal InterestRemaining => Math.Max(0m, InterestDue - PaidInterest);
    public decimal PrincipalRemaining => Math.Max(0m, PrincipalDue - PaidPrincipal);
    public bool HasAllocations => Paid > 0;

    public static ScheduleRowFigures From(RepaymentSchedule row) => new(
        row.Id,
        row.InstallmentNumber,
        row.DueDate,
        row.OpeningPrincipal,
        row.PrincipalDue,
        row.InterestDue,
        row.Allocations.Sum(a => a.PrincipalAmount),
        row.Allocations.Sum(a => a.InterestAmount));
}

public sealed record LoanBalance(
    decimal PrincipalOutstanding,
    decimal AccruedUnpaidInterest,
    decimal OverdueAmount,
    DateOnly AsOfDate)
{
    public decimal TotalOutstanding => PrincipalOutstanding + AccruedUnpaidInterest;
}

public enum SchedulePaymentStatus
{
    Upcoming,
    Due,
    Partial,
    Overdue,
    Paid
}

public static class LoanBalanceCalculator
{
    /// <summary>
    /// Principal outstanding is the loan principal less every principal allocation (installment or prepayment).
    /// Interest accrues per installment: only rows due on or before <paramref name="asOf"/> count, and nothing accrues daily.
    /// </summary>
    public static LoanBalance Calculate(
        decimal loanPrincipal,
        decimal principalPaid,
        IEnumerable<ScheduleRowFigures> currentRows,
        DateOnly asOf)
    {
        var rows = currentRows.ToList();

        return new LoanBalance(
            PrincipalOutstanding: Math.Max(0m, loanPrincipal - principalPaid),
            AccruedUnpaidInterest: rows.Where(r => r.DueDate <= asOf).Sum(r => r.InterestRemaining),
            OverdueAmount: rows.Where(r => r.DueDate < asOf).Sum(r => r.Remaining),
            AsOfDate: asOf);
    }

    public static SchedulePaymentStatus StatusOf(ScheduleRowFigures row, DateOnly asOf)
    {
        if (row.Remaining <= 0)
        {
            return SchedulePaymentStatus.Paid;
        }

        if (row.DueDate < asOf)
        {
            return SchedulePaymentStatus.Overdue;
        }

        if (row.HasAllocations)
        {
            return SchedulePaymentStatus.Partial;
        }

        return row.DueDate == asOf ? SchedulePaymentStatus.Due : SchedulePaymentStatus.Upcoming;
    }
}
