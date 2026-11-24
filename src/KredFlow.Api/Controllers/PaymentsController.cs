using Asp.Versioning;
using KredFlow.Application.Common;
using KredFlow.Application.Interfaces;
using KredFlow.Application.Payments;
using KredFlow.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KredFlow.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
[Produces("application/json")]
public class PaymentsController(IPaymentService paymentService) : ControllerBase
{
    /// <summary>Records a payment and allocates it: interest due, penalties, principal due, then upcoming installments. A repeated idempotency key returns 200 with the original payment.</summary>
    [HttpPost("loans/{loanId:guid}/payments")]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PaymentResponse>> Record(Guid loanId, CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var result = await paymentService.RecordAsync(loanId, request, cancellationToken);
        if (result.Replayed)
        {
            return Ok(result.Payment);
        }

        return CreatedAtAction(nameof(Get), new { paymentId = result.Payment.Id }, result.Payment);
    }

    /// <summary>Returns a payment with its allocations and any reversal.</summary>
    [HttpGet("payments/{paymentId:guid}")]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentResponse>> Get(Guid paymentId, CancellationToken cancellationToken) =>
        await paymentService.GetAsync(paymentId, cancellationToken);

    /// <summary>Lists a loan's payments, newest first.</summary>
    [HttpGet("loans/{loanId:guid}/payments")]
    [ProducesResponseType<PagedResponse<PaymentSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<PaymentSummaryResponse>>> ListForLoan(
        Guid loanId, [FromQuery] PaymentListQuery query, CancellationToken cancellationToken) =>
        await paymentService.ListForLoanAsync(loanId, query, cancellationToken);

    /// <summary>Lists the payments of all of a customer's loans, newest first.</summary>
    [HttpGet("customers/{customerId:guid}/payments")]
    [ProducesResponseType<PagedResponse<PaymentSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<PaymentSummaryResponse>>> ListForCustomer(
        Guid customerId, [FromQuery] PaymentListQuery query, CancellationToken cancellationToken) =>
        await paymentService.ListForCustomerAsync(customerId, query, cancellationToken);

    /// <summary>Reverses a payment. The payment is kept; its allocations stop counting toward balances.</summary>
    [HttpPost("payments/{paymentId:guid}/reversals")]
    [Authorize(Roles = RoleNames.Owner)]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PaymentResponse>> Reverse(Guid paymentId, ReversePaymentRequest request, CancellationToken cancellationToken)
    {
        var payment = await paymentService.ReverseAsync(paymentId, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { paymentId = payment.Id }, payment);
    }
}
