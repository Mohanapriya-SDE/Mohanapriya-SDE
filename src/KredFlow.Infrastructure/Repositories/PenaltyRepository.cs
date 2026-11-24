using KredFlow.Application.Interfaces;
using KredFlow.Application.Loans;
using KredFlow.Domain.Entities;
using KredFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KredFlow.Infrastructure.Repositories;

public class PenaltyRepository(KredFlowDbContext db) : IPenaltyRepository
{
    // Amounts are summed in memory: Sqlite (used in tests) cannot aggregate decimals in SQL.
    public async Task<IReadOnlyList<PenaltyFigures>> GetFiguresAsync(Guid loanId, CancellationToken cancellationToken = default)
    {
        var rows = await db.Penalties.AsNoTracking()
            .Where(p => p.LoanId == loanId)
            .Select(p => new
            {
                p.Id,
                p.ScheduleId,
                p.AssessedDate,
                p.Amount,
                p.CreatedAt,
                Waived = p.Waivers.Select(w => w.Amount).ToList()
            })
            .ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.Id).ToList();
        var paid = (await db.PaymentAllocations.AsNoTracking()
                .Where(a => a.PenaltyId != null && ids.Contains(a.PenaltyId.Value) && a.Payment.Reversal == null)
                .Select(a => new { a.PenaltyId, a.PenaltyAmount })
                .ToListAsync(cancellationToken))
            .GroupBy(a => a.PenaltyId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.PenaltyAmount));

        var scheduleIds = rows.Where(r => r.ScheduleId != null).Select(r => r.ScheduleId!.Value).ToList();
        var numbers = await db.RepaymentSchedules.AsNoTracking()
            .Where(s => scheduleIds.Contains(s.Id))
            .Select(s => new { s.Id, s.InstallmentNumber })
            .ToDictionaryAsync(s => s.Id, s => s.InstallmentNumber, cancellationToken);

        return rows.Select(r => new PenaltyFigures(
            r.Id,
            r.ScheduleId is { } sid && numbers.TryGetValue(sid, out var n) ? n : null,
            r.AssessedDate,
            r.Amount,
            r.Waived.Sum(),
            paid.GetValueOrDefault(r.Id),
            r.CreatedAt)).ToList();
    }

    public Task<Penalty?> GetByIdAsync(Guid loanId, Guid penaltyId, CancellationToken cancellationToken = default) =>
        db.Penalties.FirstOrDefaultAsync(p => p.Id == penaltyId && p.LoanId == loanId, cancellationToken);

    public void Add(Penalty penalty, LoanTransaction ledgerEntry)
    {
        db.Penalties.Add(penalty);
        db.LoanTransactions.Add(ledgerEntry);
    }

    public void AddWaiver(PenaltyWaiver waiver, LoanTransaction ledgerEntry)
    {
        db.PenaltyWaivers.Add(waiver);
        db.LoanTransactions.Add(ledgerEntry);
    }
}
