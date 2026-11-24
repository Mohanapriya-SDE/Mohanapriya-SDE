using KredFlow.Application.Common;
using KredFlow.Application.Payments;

namespace KredFlow.Application.Interfaces;

public interface IPaymentService
{
    Task<RecordPaymentResult> RecordAsync(Guid loanId, CreatePaymentRequest request, CancellationToken cancellationToken = default);

    Task<PaymentResponse> GetAsync(Guid paymentId, CancellationToken cancellationToken = default);

    Task<PagedResponse<PaymentSummaryResponse>> ListForLoanAsync(Guid loanId, PaymentListQuery query, CancellationToken cancellationToken = default);

    Task<PagedResponse<PaymentSummaryResponse>> ListForCustomerAsync(Guid customerId, PaymentListQuery query, CancellationToken cancellationToken = default);

    Task<PaymentResponse> ReverseAsync(Guid paymentId, ReversePaymentRequest request, CancellationToken cancellationToken = default);
}
