using FluentValidation;
using KredFlow.Application.Auth;
using KredFlow.Application.Interfaces;
using KredFlow.Application.Lenders;
using Microsoft.Extensions.DependencyInjection;

namespace KredFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ILenderService, LenderService>();
        services.AddValidatorsFromAssemblyContaining<RegisterLenderRequestValidator>();
        return services;
    }
}
