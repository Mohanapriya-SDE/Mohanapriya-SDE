using FluentValidation;
using KredFlow.Application.Auth;
using KredFlow.Application.Common;
using KredFlow.Application.Customers;
using KredFlow.Application.Interfaces;
using KredFlow.Application.Lenders;
using KredFlow.Application.Loans;
using Microsoft.Extensions.DependencyInjection;

namespace KredFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ILenderService, LenderService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ILoanService, LoanService>();
        services.AddScoped<ILenderClock, LenderClock>();
        services.AddValidatorsFromAssemblyContaining<RegisterLenderRequestValidator>();
        return services;
    }
}
