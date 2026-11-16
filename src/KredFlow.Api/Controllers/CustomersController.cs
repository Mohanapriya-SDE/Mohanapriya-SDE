using Asp.Versioning;
using KredFlow.Application.Common;
using KredFlow.Application.Customers;
using KredFlow.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace KredFlow.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/customers")]
[Produces("application/json")]
public class CustomersController(ICustomerService customerService) : ControllerBase
{
    /// <summary>Creates a customer for the caller's lender.</summary>
    [HttpPost]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CustomerResponse>> Create(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await customerService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { customerId = customer.Id }, customer);
    }

    /// <summary>Returns a customer of the caller's lender.</summary>
    [HttpGet("{customerId:guid}")]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerResponse>> Get(Guid customerId, CancellationToken cancellationToken) =>
        await customerService.GetAsync(customerId, cancellationToken);

    /// <summary>Lists customers, optionally filtered by name/phone search and active state, with pagination.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResponse<CustomerResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResponse<CustomerResponse>>> List([FromQuery] CustomerListQuery query, CancellationToken cancellationToken) =>
        await customerService.ListAsync(query, cancellationToken);

    /// <summary>Updates a customer's name, phone number and address.</summary>
    [HttpPut("{customerId:guid}")]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerResponse>> Update(Guid customerId, UpdateCustomerRequest request, CancellationToken cancellationToken) =>
        await customerService.UpdateAsync(customerId, request, cancellationToken);

    /// <summary>Activates or deactivates a customer. Loan and payment history is kept.</summary>
    [HttpPatch("{customerId:guid}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(Guid customerId, UpdateCustomerStatusRequest request, CancellationToken cancellationToken)
    {
        await customerService.UpdateStatusAsync(customerId, request, cancellationToken);
        return NoContent();
    }

    /// <summary>Returns the customer's loans, newest first.</summary>
    [HttpGet("{customerId:guid}/loans")]
    [ProducesResponseType<IReadOnlyList<CustomerLoanResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<CustomerLoanResponse>>> GetLoans(Guid customerId, CancellationToken cancellationToken) =>
        Ok(await customerService.GetLoansAsync(customerId, cancellationToken));

    /// <summary>Returns the customer's total outstanding across Active and Overdue loans.</summary>
    [HttpGet("{customerId:guid}/outstanding")]
    [ProducesResponseType<CustomerOutstandingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerOutstandingResponse>> GetOutstanding(Guid customerId, CancellationToken cancellationToken) =>
        await customerService.GetOutstandingAsync(customerId, cancellationToken);
}
