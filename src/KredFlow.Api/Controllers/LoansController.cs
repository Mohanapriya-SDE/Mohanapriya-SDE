using Asp.Versioning;
using KredFlow.Application.Common;
using KredFlow.Application.Interfaces;
using KredFlow.Application.Loans;
using KredFlow.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KredFlow.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/loans")]
[Produces("application/json")]
public class LoansController(ILoanService loanService) : ControllerBase
{
    /// <summary>Creates a loan and its repayment schedule in one transaction. The EMI is calculated by the backend.</summary>
    [HttpPost]
    [ProducesResponseType<LoanResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<LoanResponse>> Create(CreateLoanRequest request, CancellationToken cancellationToken)
    {
        var loan = await loanService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { loanId = loan.Id }, loan);
    }

    /// <summary>Returns a loan of the caller's lender.</summary>
    [HttpGet("{loanId:guid}")]
    [ProducesResponseType<LoanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LoanResponse>> Get(Guid loanId, CancellationToken cancellationToken) =>
        await loanService.GetAsync(loanId, cancellationToken);

    /// <summary>Lists loans with optional filters and pagination, newest start date first.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResponse<LoanResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResponse<LoanResponse>>> List([FromQuery] LoanListQuery query, CancellationToken cancellationToken) =>
        await loanService.ListAsync(query, cancellationToken);

    /// <summary>Returns the current repayment schedule with paid and remaining amounts derived from payment allocations.</summary>
    [HttpGet("{loanId:guid}/schedule")]
    [ProducesResponseType<IReadOnlyList<RepaymentScheduleResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<RepaymentScheduleResponse>>> GetSchedule(
        Guid loanId, [FromQuery] DateOnly? asOfDate, CancellationToken cancellationToken) =>
        Ok(await loanService.GetScheduleAsync(loanId, asOfDate, cancellationToken));

    /// <summary>Returns principal outstanding, accrued unpaid interest and overdue amount as of a date (default: today).</summary>
    [HttpGet("{loanId:guid}/outstanding")]
    [ProducesResponseType<LoanOutstandingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LoanOutstandingResponse>> GetOutstanding(
        Guid loanId, [FromQuery] DateOnly? asOfDate, CancellationToken cancellationToken) =>
        await loanService.GetOutstandingAsync(loanId, asOfDate, cancellationToken);

    /// <summary>Closes a loan that has no outstanding principal, interest or penalties.</summary>
    [HttpPost("{loanId:guid}/close")]
    [Authorize(Roles = RoleNames.Owner)]
    [ProducesResponseType<LoanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<LoanResponse>> Close(Guid loanId, CloseLoanRequest request, CancellationToken cancellationToken) =>
        await loanService.CloseAsync(loanId, request, cancellationToken);

    /// <summary>Posts a prepayment and recasts the remaining schedule according to the loan's prepayment policy.</summary>
    [HttpPost("{loanId:guid}/prepayments")]
    [ProducesResponseType<PrepaymentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PrepaymentResponse>> Prepay(Guid loanId, PrepaymentRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await loanService.PrepayAsync(loanId, request, cancellationToken));
}
