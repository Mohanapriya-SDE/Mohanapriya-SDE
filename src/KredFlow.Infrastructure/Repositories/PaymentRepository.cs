using KredFlow.Application.Interfaces;
using KredFlow.Application.Payments;
using KredFlow.Domain.Entities;
using KredFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KredFlow.Infrastructure.Repositories;

public class PaymentRepository(KredFlowDbContext db) : IPaymentRepository
{
    private IQueryable<Payment> Detail() => db.Payments
        .Include(p => p.Loan)
        .Include(p => p.Reversal)
        .Include(p => p.Recast)
        .Include(p => p.Allocations).ThenInclude(a => a.Schedule)
        .AsSplitQuery();

    public Task<Payment?> GetDetailAsync(Guid lenderId, Guid paymentId, CancellationToken cancellationToken = default) =>
        Detail().FirstOrDefaultAsync(p => p.Id == paymentId && p.Loan.LenderId == lenderId, cancellationToken);

    public Task<Payment?> GetByIdempotencyKeyAsync(Guid loanId, string key, CancellationToken cancellationToken = default) =>
        Detail().FirstOrDefaultAsync(p => p.LoanId == loanId && p.IdempotencyKey == key, cancellationToken);

    public Task<(IReadOnlyList<Payment> Items, int TotalCount)> ListForLoanAsync(
        Guid loanId, PaymentListQuery query, CancellationToken cancellationToken = default) =>
        PageAsync(db.Payments.Where(p => p.LoanId == loanId), query, cancellationToken);

    public Task<(IReadOnlyList<Payment> Items, int TotalCount)> ListForCustomerAsync(
        Guid lenderId, Guid customerId, PaymentListQuery query, CancellationToken cancellationToken = default) =>
        PageAsync(db.Payments.Where(p => p.Loan.CustomerId == customerId && p.Loan.LenderId == lenderId), query, cancellationToken);

    public async Task<IReadOnlyList<Payment>> GetActiveLaterPaymentsAsync(Payment payment, CancellationToken cancellationToken = default) =>
        await db.Payments.AsNoTracking()
            .Where(p => p.LoanId == payment.LoanId && p.Id != payment.Id && p.Reversal == null
                        && (p.PaymentDate > payment.PaymentDate
                            || (p.PaymentDate == payment.PaymentDate && p.CreatedAt > payment.CreatedAt)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RepaymentSchedule>> GetRecastRowsAsync(Guid paymentId, CancellationToken cancellationToken = default) =>
        await db.RepaymentSchedules
            .Where(r => r.CreatedByPaymentId == paymentId || r.SupersededByPaymentId == paymentId)
            .ToListAsync(cancellationToken);

    public void Add(Payment payment, IEnumerable<PaymentAllocation> allocations, LoanTransaction ledgerEntry, LoanRecast? recast = null)
    {
        db.Payments.Add(payment);
        db.PaymentAllocations.AddRange(allocations);
        db.LoanTransactions.Add(ledgerEntry);
        if (recast is not null)
        {
            db.LoanRecasts.Add(recast);
        }
    }

    public void AddReversal(PaymentReversal reversal, LoanTransaction ledgerEntry)
    {
        db.PaymentReversals.Add(reversal);
        db.LoanTransactions.Add(ledgerEntry);
    }

    public void AddRows(IEnumerable<RepaymentSchedule> rows) => db.RepaymentSchedules.AddRange(rows);

    private static async Task<(IReadOnlyList<Payment> Items, int TotalCount)> PageAsync(
        IQueryable<Payment> source, PaymentListQuery q, CancellationToken cancellationToken)
    {
        var query = source;

        if (q.Status == PaymentStatus.Recorded)
        {
            query = query.Where(p => p.Reversal == null);
        }
        else if (q.Status == PaymentStatus.Reversed)
        {
            query = query.Where(p => p.Reversal != null);
        }

        if (q.PaymentMethod is { } method)
        {
            query = query.Where(p => p.PaymentMethod == method);
        }

        if (q.From is { } from)
        {
            query = query.Where(p => p.PaymentDate >= from);
        }

        if (q.To is { } to)
        {
            query = query.Where(p => p.PaymentDate <= to);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Include(p => p.Loan)
            .Include(p => p.Reversal)
            .AsNoTracking()
            .OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.CreatedAt).ThenBy(p => p.Id)
            .Skip((q.Page - 1) * q.PageSize)
            .Take(q.PageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }
}
