using KredFlow.Application.Penalties;

namespace KredFlow.Application.Interfaces;

public interface IPenaltyService
{
    Task<IReadOnlyList<PenaltyResponse>> ListAsync(Guid loanId, CancellationToken cancellationToken = default);

    Task<PenaltyResponse> AssessAsync(Guid loanId, AssessPenaltyRequest request, CancellationToken cancellationToken = default);

    Task<PenaltyResponse> WaiveAsync(Guid loanId, Guid penaltyId, WaivePenaltyRequest request, CancellationToken cancellationToken = default);
}
