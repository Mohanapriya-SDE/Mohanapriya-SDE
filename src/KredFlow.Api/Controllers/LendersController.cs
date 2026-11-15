using Asp.Versioning;
using KredFlow.Application.Interfaces;
using KredFlow.Application.Lenders;
using KredFlow.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KredFlow.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/lenders")]
[Produces("application/json")]
public class LendersController(ILenderService lenderService) : ControllerBase
{
    /// <summary>Returns the profile of the caller's lender.</summary>
    [HttpGet("me")]
    [ProducesResponseType<LenderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LenderResponse>> GetMe(CancellationToken cancellationToken) =>
        await lenderService.GetMeAsync(cancellationToken);

    /// <summary>Updates the business name, phone, address and time zone of the caller's lender.</summary>
    [HttpPut("me")]
    [Authorize(Roles = RoleNames.Owner)]
    [ProducesResponseType<LenderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<LenderResponse>> UpdateMe(UpdateLenderRequest request, CancellationToken cancellationToken) =>
        await lenderService.UpdateMeAsync(request, cancellationToken);

    /// <summary>Deactivates the caller's lender. Historical data is kept; reactivation is not available here.</summary>
    [HttpPatch("me/status")]
    [Authorize(Roles = RoleNames.Owner)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateStatus(UpdateLenderStatusRequest request, CancellationToken cancellationToken)
    {
        await lenderService.DeactivateAsync(request, cancellationToken);
        return NoContent();
    }
}
