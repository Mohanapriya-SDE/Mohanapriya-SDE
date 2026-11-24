using KredFlow.Application.Loans;
using KredFlow.Domain.Entities;

namespace KredFlow.Application.Interfaces;

public interface ILoanRepository
{
    /// <summary>Returns the tracked loan only if it belongs to the lender.</summary>
    Task<Loan?> GetByIdAsync(Guid lenderId, Guid loanId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Loan> Items, int TotalCount)> ListAsync(
        Guid lenderId, LoanListQuery query, CancellationToken cancellationToken = default);

    /// <summary>Of the given loans, the ones that ever had a schedule row (current or superseded).</summary>
    Task<HashSet<Guid>> GetLoanIdsWithScheduleAsync(IReadOnlyCollection<Guid> loanIds, CancellationToken cancellationToken = default);

    /// <summary>Tracked current (not superseded) schedule rows, in installment order, with the allocations of non-reversed payments.</summary>
    Task<IReadOnlyList<RepaymentSchedule>> GetCurrentScheduleAsync(Guid loanId, CancellationToken cancellationToken = default);

    /// <summary>Principal allocated by every non-reversed payment of the loan, installment and prepayment alike.</summary>
    Task<decimal> GetPrincipalPaidAsync(Guid loanId, CancellationToken cancellationToken = default);

    /// <summary>The latest payment date among non-reversed payments.</summary>
    Task<DateOnly?> GetLastPaymentDateAsync(Guid loanId, CancellationToken cancellationToken = default);

    /// <summary>Tracks the loan, its schedule and ledger entries; they are persisted by the next save.</summary>
    void Add(Loan loan, IEnumerable<RepaymentSchedule> schedule, LoanTransaction disbursement);
}
