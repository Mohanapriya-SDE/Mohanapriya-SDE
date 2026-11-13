using KredFlow.Application.Interfaces;
using KredFlow.Infrastructure.Persistence;
using KredFlow.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KredFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<KredFlowDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IDatabaseSeeder, DatabaseSeeder>();
        return services;
    }
}
