using KredFlow.Application.Auth;
using KredFlow.Application.Interfaces;
using KredFlow.Domain.Entities;
using KredFlow.Infrastructure.Persistence;
using KredFlow.Infrastructure.Persistence.Seed;
using KredFlow.Infrastructure.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KredFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<KredFlowDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IDatabaseSeeder, DatabaseSeeder>();
        services.AddScoped<ILenderRepository, LenderRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                options.Password.RequiredLength = PasswordPolicy.MinLength;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<KredFlowDbContext>();

        return services;
    }
}
