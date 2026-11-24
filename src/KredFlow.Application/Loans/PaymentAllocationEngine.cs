using KredFlow.Application.Exceptions;
using KredFlow.Domain.Enums;

namespace KredFlow.Application.Loans;

public sealed record AllocationLine(
    Guid? ScheduleId,
    decimal Interest,
    decimal Principal,
    AllocationType Type,
    Guid? PenaltyId = null,
    decimal Penalty = 0m)
{
    public decimal Total => Interest + Principal + Penalty;
}

/// <summary>A penalty with what has been paid and waived. Outstanding is always derived.</summary>
public sealed record PenaltyFigures(
    Guid Id,
    int? InstallmentNumber,
    DateOnly AssessedDate,
    decimal Amount,
    decimal Waived,
    decimal Paid,
    DateTime CreatedAt = default)
{
    public decimal Outstanding => Math.Max(0m, Amount - Waived - Paid);
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
    /// Allocates a regular payment as of <paramref name="paymentDate"/>, oldest first within each step:
    /// interest due, then penalties, then principal due, then any remainder as an advance on upcoming installments
    /// (interest before principal). The schedule is never recast. The payment may not exceed everything still payable.
    /// </summary>
    public static List<AllocationLine> AllocatePayment(
        IReadOnlyList<ScheduleRowFigures> rows,
        IReadOnlyList<PenaltyFigures> penalties,
        decimal amount,
        DateOnly paymentDate)
    {
        var maxPayable = rows.Sum(r => r.Remaining) + penalties.Sum(p => p.Outstanding);
        if (amount > maxPayable)
        {
            throw new BusinessRuleException($"The amount exceeds the total still payable of {maxPayable:0.00}.");
        }

        var dueRows = rows.Where(r => r.DueDate <= paymentDate).OrderBy(r => r.InstallmentNumber).ToList();
        var interest = new Dictionary<Guid, decimal>();
        var principal = new Dictionary<Guid, decimal>();
        var penaltyLines = new List<AllocationLine>();
        var left = amount;

        foreach (var row in dueRows)
        {
            var take = Math.Min(left, row.InterestRemaining);
            if (take > 0)
            {
                interest[row.Id] = take;
                left -= take;
            }
        }

        foreach (var penalty in penalties.OrderBy(p => p.AssessedDate).ThenBy(p => p.Id))
        {
            var take = Math.Min(left, penalty.Outstanding);
            if (take > 0)
            {
                penaltyLines.Add(new AllocationLine(null, 0m, 0m, AllocationType.Penalty, penalty.Id, take));
                left -= take;
            }
        }

        foreach (var row in dueRows)
        {
            var take = Math.Min(left, row.PrincipalRemaining);
            if (take > 0)
            {
                principal[row.Id] = take;
                left -= take;
            }
        }

        var (advance, leftover) = AllocateToInstallments(rows.Where(r => r.DueDate > paymentDate), left);
        if (leftover > 0)
        {
            throw new InvalidOperationException("Payment allocation left an unallocated amount.");
        }

        var lines = new List<AllocationLine>();
        foreach (var row in rows.OrderBy(r => r.InstallmentNumber))
        {
            var advanced = advance.FirstOrDefault(a => a.ScheduleId == row.Id);
            var i = interest.GetValueOrDefault(row.Id) + (advanced?.Interest ?? 0m);
            var p = principal.GetValueOrDefault(row.Id) + (advanced?.Principal ?? 0m);
            if (i + p > 0)
            {
                lines.Add(new AllocationLine(row.Id, i, p, AllocationType.Installment));
            }
        }

        lines.AddRange(penaltyLines);
        return lines;
    }

    /// <summary>
    /// A prepayment first settles any partly paid future installment, then the rest is principal paid ahead of schedule.
    /// Installments already due must be fully paid beforehand, so a prepayment never hides arrears.
    /// </summary>
    public static PrepaymentPlan PlanPrepayment(
        IReadOnlyList<ScheduleRowFigures> currentRows,
        decimal principalOutstanding,
        decimal amount,
        DateOnly paymentDate,
        decimal outstandingPenalties = 0m)
    {
        if (outstandingPenalties > 0)
        {
            throw new BusinessRuleException(
                $"Penalties of {outstandingPenalties:0.00} must be paid or waived before a prepayment.");
        }

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
