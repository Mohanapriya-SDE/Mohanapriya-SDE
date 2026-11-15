using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace KredFlow.Api.Auth;

public static class OpenApiBearerExtensions
{
    public static OpenApiOptions AddBearerAuthentication(this OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.AddComponent("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "JWT access token from POST /api/v1/auth/login."
            });
            document.Security ??= [];
            document.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
            return Task.CompletedTask;
        });
        return options;
    }
}
