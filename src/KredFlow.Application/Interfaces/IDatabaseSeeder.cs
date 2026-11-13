namespace KredFlow.Application.Interfaces;

public interface IDatabaseSeeder
{
    /// <summary>Inserts sample data; does nothing if data already exists.</summary>
    Task SeedAsync(CancellationToken cancellationToken = default);
}
