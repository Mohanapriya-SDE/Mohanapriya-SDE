using KredFlow.Application.Exceptions;
using KredFlow.Domain.Enums;

namespace KredFlow.Application.Loans;

public sealed record AllocationLine(Guid? ScheduleId, decimal Interest, decimal Principal, AllocationType Type)
{
    public decimal Total => Interest + Principal;
}

public sealed record PrepaymentPlan(
    IReadOnlyList<AllocationLine> Lines,
    decimal PrincipalPrepaid,
    decimal NewPrincipalBalance,
    int LastKeptInstallmentNumber,
    IReadOnlyList<Guid> ReplacedRowIds);

/// <summary>Decides how a received amount is split across installments. Shared by prepayments and, later, regular payments.</summary>
public static class PaymentAllocationEngine
{
    /// <summary>Oldest installment first; within an installment interest is settled before principal.</summary>
    public static (List<AllocationLine> Lines, decimal Leftover) AllocateToInstallments(
        IEnumerable<ScheduleRowFigures> rows, decimal amount)
    {
        var lines = new List<AllocationLine>();
        var left = amount;

        foreach (var row in rows.OrderBy(r => r.InstallmentNumber))
        {
            if (left <= 0)
            {
                break;
            }

            var interest = Math.Min(left, row.InterestRemaining);
            left -= interest;
            var principal = Math.Min(left, row.PrincipalRemaining);
            left -= principal;

            if (interest + principal > 0)
            {
                lines.Add(new AllocationLine(row.Id, interest, principal, AllocationType.Installment));
            }
        }

        return (lines, left);
    }

    /// <summary>
    /// A prepayment first settles any partly paid future installment, then the rest is principal paid ahead of schedule.
    /// Installments already due must be fully paid beforehand, so a prepayment never hides arrears.
    /// </summary>
    public static PrepaymentPlan PlanPrepayment(
        IReadOnlyList<ScheduleRowFigures> currentRows,
        decimal principalOutstanding,
        decimal amount,
        DateOnly paymentDate)
    {
        var unpaidDues = currentRows.Where(r => r.DueDate <= paymentDate).Sum(r => r.Remaining);
        if (unpaidDues > 0)
        {
            throw new BusinessRuleException(
                $"Installments due on or before {paymentDate:yyyy-MM-dd} must be settled before a prepayment. Unpaid: {unpaidDues:0.00}.");
        }

        var partlyPaid = currentRows.Where(r => r.DueDate > paymentDate && r.HasAllocations && r.Remaining > 0).ToList();
        var payoff = principalOutstanding + partlyPaid.Sum(r => r.InterestRemaining);
        if (amount > payoff)
        {
            throw new BusinessRuleException($"The amount exceeds the payoff amount of {payoff:0.00}.");
        }

        var (lines, prepaid) = AllocateToInstallments(partlyPaid, amount);
        if (prepaid <= 0)
        {
            throw new BusinessRuleException("The amount does not exceed what is still due on a partly paid installment. Record it as a regular payment instead.");
        }

        lines.Add(new AllocationLine(null, 0m, prepaid, AllocationType.PrincipalPrepayment));

        var newBalance = principalOutstanding - lines.Sum(l => l.Principal);
        var lastKept = currentRows
            .Where(r => r.DueDate <= paymentDate || r.HasAllocations)
            .Select(r => r.InstallmentNumber)
            .DefaultIfEmpty(0)
            .Max();
        var replaced = currentRows.Where(r => r.InstallmentNumber > lastKept).Select(r => r.Id).ToList();

        return new PrepaymentPlan(lines, prepaid, newBalance, lastKept, replaced);
    }
}
