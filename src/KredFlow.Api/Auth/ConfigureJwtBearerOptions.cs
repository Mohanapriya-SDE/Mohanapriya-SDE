using System.Text;
using KredFlow.Application.Interfaces;
using KredFlow.Domain.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace KredFlow.Api.Auth;

/// <summary>Configures JWT validation lazily so the signing key is read from the final configuration.</summary>
public class ConfigureJwtBearerOptions(IOptions<JwtOptions> jwtOptions) : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        var jwt = jwtOptions.Value;

        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.Sub,
            RoleClaimType = AuthClaimTypes.Role
        };

        options.Events = new JwtBearerEvents { OnTokenValidated = RejectInactiveAccounts };
    }

    public void Configure(JwtBearerOptions options) => Configure(JwtBearerDefaults.AuthenticationScheme, options);

    // A valid signature isn't enough: deactivation of the user or lender must take effect immediately.
    private static async Task RejectInactiveAccounts(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var userId = principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var lenderId = principal?.FindFirst(AuthClaimTypes.LenderId)?.Value;
        if (!Guid.TryParse(userId, out _) || !Guid.TryParse(lenderId, out var tokenLenderId))
        {
            context.Fail("Token is missing required claims.");
            return;
        }

        var services = context.HttpContext.RequestServices;
        var user = await services.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(userId!);
        var lender = user is null
            ? null
            : await services.GetRequiredService<ILenderRepository>().GetByIdAsync(user.LenderId, context.HttpContext.RequestAborted);

        if (user is null || !user.IsActive || user.LenderId != tokenLenderId || lender is null || !lender.IsActive)
        {
            context.Fail("Account is inactive.");
        }
    }
}
