using KredFlow.Application.Common;
using KredFlow.Application.Customers;

namespace KredFlow.Application.Interfaces;

public interface ICustomerService
{
    Task<CustomerResponse> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default);

    Task<CustomerResponse> GetAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task<PagedResponse<CustomerResponse>> ListAsync(CustomerListQuery query, CancellationToken cancellationToken = default);

    Task<CustomerResponse> UpdateAsync(Guid customerId, UpdateCustomerRequest request, CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(Guid customerId, UpdateCustomerStatusRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerLoanResponse>> GetLoansAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task<CustomerOutstandingResponse> GetOutstandingAsync(Guid customerId, CancellationToken cancellationToken = default);
}
