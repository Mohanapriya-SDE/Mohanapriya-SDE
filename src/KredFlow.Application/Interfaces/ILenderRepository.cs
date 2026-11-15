using KredFlow.Domain.Entities;

namespace KredFlow.Application.Interfaces;

public interface ILenderRepository
{
    Task<Lender?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Tracks the lender; it is persisted by the next save (e.g. by creating the user).</summary>
    void Add(Lender lender);
}
