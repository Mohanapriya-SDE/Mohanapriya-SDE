using System.Data.Common;
using KredFlow.Application.Exceptions;
using KredFlow.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace KredFlow.Infrastructure.Persistence;

public class UnitOfWork(KredFlowDbContext db) : IUnitOfWork
{
    private const string UniqueViolationSqlState = "23505";

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        try
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                await action(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });
        }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: UniqueViolationSqlState })
        {
            throw new ConflictException("A record with the same unique value already exists.");
        }
    }
}
