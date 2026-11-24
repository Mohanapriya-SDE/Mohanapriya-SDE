using KredFlow.Application.Common;
using KredFlow.Application.Loans;

namespace KredFlow.Application.Interfaces;

public interface ILoanService
{
    Task<LoanResponse> CreateAsync(CreateLoanRequest request, CancellationToken cancellationToken = default);

    Task<LoanResponse> GetAsync(Guid loanId, CancellationToken cancellationToken = default);

    Task<PagedResponse<LoanResponse>> ListAsync(LoanListQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RepaymentScheduleResponse>> GetScheduleAsync(Guid loanId, DateOnly? asOfDate, CancellationToken cancellationToken = default);

    Task<LoanOutstandingResponse> GetOutstandingAsync(Guid loanId, DateOnly? asOfDate, CancellationToken cancellationToken = default);

    Task<LoanResponse> CloseAsync(Guid loanId, CloseLoanRequest request, CancellationToken cancellationToken = default);

    Task<PrepaymentResult> PrepayAsync(Guid loanId, PrepaymentRequest request, CancellationToken cancellationToken = default);
}
