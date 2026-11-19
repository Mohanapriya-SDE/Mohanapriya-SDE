using KredFlow.Application.Interfaces;
using KredFlow.Application.Loans;
using KredFlow.Domain.Entities;
using KredFlow.Domain.Enums;
using KredFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KredFlow.Infrastructure.Repositories;

public class LoanRepository(KredFlowDbContext db) : ILoanRepository
{
    public Task<Loan?> GetByIdAsync(Guid lenderId, Guid loanId, CancellationToken cancellationToken = default) =>
        db.Loans.FirstOrDefaultAsync(x => x.Id == loanId && x.LenderId == lenderId, cancellationToken);

    public async Task<(IReadOnlyList<Loan> Items, int TotalCount)> ListAsync(
        Guid lenderId, LoanListQuery q, CancellationToken cancellationToken = default)
    {
        var query = db.Loans.AsNoTracking().Where(x => x.LenderId == lenderId);

        if (q.CustomerId is { } customerId)
        {
            query = query.Where(x => x.CustomerId == customerId);
        }

        if (q.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (q.MinPrincipal is { } min)
        {
            query = query.Where(x => x.Principal >= min);
        }

        if (q.MaxPrincipal is { } max)
        {
            query = query.Where(x => x.Principal <= max);
        }

        if (q.StartDateFrom is { } from)
        {
            query = query.Where(x => x.StartDate >= from);
        }

        if (q.StartDateTo is { } to)
        {
            query = query.Where(x => x.StartDate <= to);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.StartDate).ThenBy(x => x.Id)
            .Skip((q.Page - 1) * q.PageSize)
            .Take(q.PageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    // Superseded rows count too: a fully prepaid loan still had its schedule generated.
    public async Task<HashSet<Guid>> GetLoanIdsWithScheduleAsync(IReadOnlyCollection<Guid> loanIds, CancellationToken cancellationToken = default)
    {
        var ids = await db.RepaymentSchedules.AsNoTracking()
            .Where(s => loanIds.Contains(s.LoanId))
            .Select(s => s.LoanId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    public async Task<IReadOnlyList<RepaymentSchedule>> GetCurrentScheduleAsync(Guid loanId, CancellationToken cancellationToken = default) =>
        await db.RepaymentSchedules
            .Include(s => s.Allocations)
            .Where(s => s.LoanId == loanId && !s.IsSuperseded)
            .OrderBy(s => s.InstallmentNumber)
            .ToListAsync(cancellationToken);

    // Summed in memory: Sqlite (used in tests) cannot aggregate decimals in SQL.
    public async Task<decimal> GetPrincipalPaidAsync(Guid loanId, CancellationToken cancellationToken = default) =>
        (await db.PaymentAllocations.AsNoTracking()
            .Where(a => a.Payment.LoanId == loanId)
            .Select(a => a.PrincipalAmount)
            .ToListAsync(cancellationToken)).Sum();

    public async Task<DateOnly?> GetLastPaymentDateAsync(Guid loanId, CancellationToken cancellationToken = default)
    {
        var dates = await db.Payments.AsNoTracking()
            .Where(p => p.LoanId == loanId)
            .Select(p => p.PaymentDate)
            .ToListAsync(cancellationToken);
        return dates.Count == 0 ? null : dates.Max();
    }

    public async Task<decimal> GetNetPenaltiesAsync(Guid loanId, CancellationToken cancellationToken = default)
    {
        var entries = await db.LoanTransactions.AsNoTracking()
            .Where(t => t.LoanId == loanId
                        && (t.TransactionType == TransactionType.Penalty || t.TransactionType == TransactionType.Waiver))
            .Select(t => new { t.TransactionType, t.Amount })
            .ToListAsync(cancellationToken);

        return entries.Sum(e => e.TransactionType == TransactionType.Penalty ? e.Amount : -e.Amount);
    }

    public void Add(Loan loan, IEnumerable<RepaymentSchedule> schedule, LoanTransaction disbursement)
    {
        db.Loans.Add(loan);
        db.RepaymentSchedules.AddRange(schedule);
        db.LoanTransactions.Add(disbursement);
    }

    public void AddRows(IEnumerable<RepaymentSchedule> rows) => db.RepaymentSchedules.AddRange(rows);

    public void AddPayment(Payment payment, IEnumerable<PaymentAllocation> allocations, LoanTransaction transaction)
    {
        db.Payments.Add(payment);
        db.PaymentAllocations.AddRange(allocations);
        db.LoanTransactions.Add(transaction);
    }
}
