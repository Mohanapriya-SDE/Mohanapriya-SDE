using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace KredFlow.Api.RateLimiting;

public sealed class PolicyLimit
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
}

public sealed class AuthRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public PolicyLimit Login { get; set; } = new() { PermitLimit = 5, WindowSeconds = 60 };
    public PolicyLimit Register { get; set; } = new() { PermitLimit = 3, WindowSeconds = 600 };
}

public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string Register = "register";
}

public static class RateLimitingExtensions
{
    public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<AuthRateLimitOptions>().BindConfiguration(AuthRateLimitOptions.SectionName);

        // Configured through options so limits are read from the final configuration (and can be lowered in tests).
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<AuthRateLimitOptions>>((limiter, settings) =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(RateLimitPolicies.Login, http => ByClientIp(http, settings.Value.Login));
            limiter.AddPolicy(RateLimitPolicies.Register, http => ByClientIp(http, settings.Value.Register));
            limiter.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                var problem = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problem.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests.",
                        Detail = "Rate limit exceeded. Try again later."
                    }
                });
            };
        });

        services.AddRateLimiter(_ => { });
        return services;
    }

    private static RateLimitPartition<string> ByClientIp(HttpContext http, PolicyLimit limit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit.PermitLimit,
                Window = TimeSpan.FromSeconds(limit.WindowSeconds),
                QueueLimit = 0
            });
}
