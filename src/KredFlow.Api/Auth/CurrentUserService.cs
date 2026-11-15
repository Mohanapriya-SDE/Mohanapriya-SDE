using KredFlow.Application.Exceptions;
using KredFlow.Application.Interfaces;
using Microsoft.IdentityModel.JsonWebTokens;

namespace KredFlow.Api.Auth;

public class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public Guid UserId => ReadGuid(JwtRegisteredClaimNames.Sub);

    public Guid LenderId => ReadGuid(AuthClaimTypes.LenderId);

    private Guid ReadGuid(string claimType)
    {
        var value = httpContextAccessor.HttpContext?.User.FindFirst(claimType)?.Value;
        return Guid.TryParse(value, out var id) ? id : throw new AuthenticationFailedException("Authentication is required.");
    }
}
