using KredFlow.Application.Interfaces;
using KredFlow.Domain.Entities;
using KredFlow.Domain.Enums;
using KredFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KredFlow.Infrastructure.Repositories;

public class LenderRepository(KredFlowDbContext db) : ILenderRepository
{
    public Task<Lender?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Lenders.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public void Add(Lender lender) => db.Lenders.Add(lender);

    public Task<bool> HasOpenLoansAsync(Guid lenderId, CancellationToken cancellationToken = default) =>
        db.Loans.AnyAsync(
            l => l.LenderId == lenderId && (l.Status == LoanStatus.Active || l.Status == LoanStatus.Overdue),
            cancellationToken);
}
