using KredFlow.Application.Interfaces;
using KredFlow.Infrastructure;
using KredFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(
    builder.Configuration.GetConnectionString("KredFlow")
    ?? throw new InvalidOperationException("Connection string 'KredFlow' is not configured."));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<KredFlowDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<IDatabaseSeeder>().SeedAsync();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
