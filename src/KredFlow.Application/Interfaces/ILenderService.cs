using KredFlow.Application.Lenders;

namespace KredFlow.Application.Interfaces;

public interface ILenderService
{
    Task<LenderResponse> GetMeAsync(CancellationToken cancellationToken = default);

    Task<LenderResponse> UpdateMeAsync(UpdateLenderRequest request, CancellationToken cancellationToken = default);

    Task DeactivateAsync(UpdateLenderStatusRequest request, CancellationToken cancellationToken = default);
}
