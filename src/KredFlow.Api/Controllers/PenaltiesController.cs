using Asp.Versioning;
using KredFlow.Application.Interfaces;
using KredFlow.Application.Penalties;
using KredFlow.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KredFlow.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/loans/{loanId:guid}/penalties")]
[Produces("application/json")]
public class PenaltiesController(IPenaltyService penaltyService) : ControllerBase
{
    /// <summary>Lists a loan's penalties with paid, waived and outstanding amounts.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PenaltyResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<PenaltyResponse>>> List(Guid loanId, CancellationToken cancellationToken) =>
        Ok(await penaltyService.ListAsync(loanId, cancellationToken));

    /// <summary>Assesses a penalty on a loan.</summary>
    [HttpPost]
    [Authorize(Roles = RoleNames.Owner)]
    [ProducesResponseType<PenaltyResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PenaltyResponse>> Assess(Guid loanId, AssessPenaltyRequest request, CancellationToken cancellationToken)
    {
        var penalty = await penaltyService.AssessAsync(loanId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, penalty);
    }

    /// <summary>Waives all or part of a penalty's outstanding amount. Waivers cannot be undone.</summary>
    [HttpPost("{penaltyId:guid}/waivers")]
    [Authorize(Roles = RoleNames.Owner)]
    [ProducesResponseType<PenaltyResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PenaltyResponse>> Waive(
        Guid loanId, Guid penaltyId, WaivePenaltyRequest request, CancellationToken cancellationToken)
    {
        var penalty = await penaltyService.WaiveAsync(loanId, penaltyId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, penalty);
    }
}
