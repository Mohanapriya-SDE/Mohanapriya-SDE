using FluentValidation;
using KredFlow.Application.Auth;
using KredFlow.Application.Customers;
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
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddValidatorsFromAssemblyContaining<RegisterLenderRequestValidator>();
        return services;
    }
}
