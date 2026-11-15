using System.ComponentModel.DataAnnotations;

namespace KredFlow.Api.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int AccessTokenExpiryMinutes { get; set; } = 15;

    // HS256 needs at least 256 bits; supplied via user-secrets or the Jwt__SigningKey environment variable.
    [Required, MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;
}

public static class AuthClaimTypes
{
    public const string LenderId = "lenderId";
    public const string Role = "role";
}
