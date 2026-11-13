using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KredFlow.Infrastructure.Persistence;

// Lets `dotnet ef` create the context without running the API startup (migrate/seed).
public class KredFlowDbContextFactory : IDesignTimeDbContextFactory<KredFlowDbContext>
{
    public KredFlowDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<KredFlowDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=kredflow_advanced_api;Username=mohanac")
            .Options;
        return new KredFlowDbContext(options);
    }
}
