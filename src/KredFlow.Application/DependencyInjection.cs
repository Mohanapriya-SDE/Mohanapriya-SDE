using FluentValidation;
using KredFlow.Application.Auth;
using KredFlow.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace KredFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddValidatorsFromAssemblyContaining<RegisterLenderRequestValidator>();
        return services;
    }
}
