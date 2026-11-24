using KredFlow.Application.Payments;
using KredFlow.Domain.Entities;

namespace KredFlow.Application.Interfaces;

public interface IPaymentRepository
{
    /// <summary>The payment with allocations, reversal, recast and loan, only if its loan belongs to the lender.</summary>
    Task<Payment?> GetDetailAsync(Guid lenderId, Guid paymentId, CancellationToken cancellationToken = default);

    Task<Payment?> GetByIdempotencyKeyAsync(Guid loanId, string key, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Payment> Items, int TotalCount)> ListForLoanAsync(
        Guid loanId, PaymentListQuery query, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Payment> Items, int TotalCount)> ListForCustomerAsync(
        Guid lenderId, Guid customerId, PaymentListQuery query, CancellationToken cancellationToken = default);

    /// <summary>Non-reversed payments of the loan recorded after the given one (by payment date, then creation time).</summary>
    Task<IReadOnlyList<Payment>> GetActiveLaterPaymentsAsync(Payment payment, CancellationToken cancellationToken = default);

    /// <summary>Tracked schedule rows that a prepayment's recast generated or replaced.</summary>
    Task<IReadOnlyList<RepaymentSchedule>> GetRecastRowsAsync(Guid paymentId, CancellationToken cancellationToken = default);

    /// <summary>Tracks the payment, its allocations, its ledger entry and (for a prepayment) its recast.</summary>
    void Add(Payment payment, IEnumerable<PaymentAllocation> allocations, LoanTransaction ledgerEntry, LoanRecast? recast = null);

    void AddReversal(PaymentReversal reversal, LoanTransaction ledgerEntry);

    void AddRows(IEnumerable<RepaymentSchedule> rows);
}
