namespace KredFlow.Application.Interfaces;

public interface IUnitOfWork
{
    /// <summary>Runs the action in one database transaction; any exception rolls it back.</summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);
}
