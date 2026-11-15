using KredFlow.Domain.Entities;

namespace KredFlow.Application.Interfaces;

public record AccessToken(string Value, int ExpiresInSeconds);

public interface IJwtTokenService
{
    AccessToken GenerateToken(ApplicationUser user, Lender lender, IEnumerable<string> roles);
}
