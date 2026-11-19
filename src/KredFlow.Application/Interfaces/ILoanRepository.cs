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

    /// <summary>Tracked current (not superseded) schedule rows with their allocations, in installment order.</summary>
    Task<IReadOnlyList<RepaymentSchedule>> GetCurrentScheduleAsync(Guid loanId, CancellationToken cancellationToken = default);

    /// <summary>Principal allocated by every payment of the loan, installment and prepayment alike.</summary>
    Task<decimal> GetPrincipalPaidAsync(Guid loanId, CancellationToken cancellationToken = default);

    Task<DateOnly?> GetLastPaymentDateAsync(Guid loanId, CancellationToken cancellationToken = default);

    /// <summary>Penalty ledger entries minus waivers.</summary>
    Task<decimal> GetNetPenaltiesAsync(Guid loanId, CancellationToken cancellationToken = default);

    /// <summary>Tracks the loan, its schedule and ledger entries; they are persisted by the next save.</summary>
    void Add(Loan loan, IEnumerable<RepaymentSchedule> schedule, LoanTransaction disbursement);

    void AddRows(IEnumerable<RepaymentSchedule> rows);

    void AddPayment(Payment payment, IEnumerable<PaymentAllocation> allocations, LoanTransaction transaction);
}
