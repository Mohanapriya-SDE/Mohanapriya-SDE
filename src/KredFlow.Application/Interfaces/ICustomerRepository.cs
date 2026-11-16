using KredFlow.Domain.Entities;

namespace KredFlow.Application.Interfaces;

public interface ICustomerRepository
{
    /// <summary>Returns the tracked customer only if it belongs to the lender.</summary>
    Task<Customer?> GetByIdAsync(Guid lenderId, Guid customerId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Customer> Items, int TotalCount)> ListAsync(
        Guid lenderId, string? search, bool? isActive, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Tracks the customer; it is persisted by the next save.</summary>
    void Add(Customer customer);

    Task<IReadOnlyList<Loan>> GetLoansAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>Open (Active or Overdue) loan count and the unpaid schedule amount across them.</summary>
    Task<(int OpenLoanCount, decimal TotalOutstanding)> GetOutstandingAsync(Guid customerId, CancellationToken cancellationToken = default);
}
