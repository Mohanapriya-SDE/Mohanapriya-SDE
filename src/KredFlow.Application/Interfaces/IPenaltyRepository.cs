using KredFlow.Application.Loans;
using KredFlow.Domain.Entities;

namespace KredFlow.Application.Interfaces;

public interface IPenaltyRepository
{
    /// <summary>Every penalty of the loan with what has been paid (by non-reversed payments) and waived.</summary>
    Task<IReadOnlyList<PenaltyFigures>> GetFiguresAsync(Guid loanId, CancellationToken cancellationToken = default);

    /// <summary>The tracked penalty, only if it belongs to the loan.</summary>
    Task<Penalty?> GetByIdAsync(Guid loanId, Guid penaltyId, CancellationToken cancellationToken = default);

    /// <summary>Tracks the penalty and its ledger entry.</summary>
    void Add(Penalty penalty, LoanTransaction ledgerEntry);

    /// <summary>Tracks the waiver and its ledger entry.</summary>
    void AddWaiver(PenaltyWaiver waiver, LoanTransaction ledgerEntry);
}
