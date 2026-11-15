using KredFlow.Application.Auth;

namespace KredFlow.Application.Interfaces;

public interface IAuthService
{
    Task<RegisterLenderResponse> RegisterAsync(RegisterLenderRequest request, CancellationToken cancellationToken = default);

    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<UserInfoResponse> GetCurrentUserAsync(CancellationToken cancellationToken = default);
}
