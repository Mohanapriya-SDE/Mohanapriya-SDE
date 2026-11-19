using KredFlow.Application.Interfaces;
using KredFlow.Domain.Entities;
using KredFlow.Domain.Enums;
using KredFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KredFlow.Infrastructure.Repositories;

public class CustomerRepository(KredFlowDbContext db) : ICustomerRepository
{
    public Task<Customer?> GetByIdAsync(Guid lenderId, Guid customerId, CancellationToken cancellationToken = default) =>
        db.Customers.FirstOrDefaultAsync(x => x.Id == customerId && x.LenderId == lenderId, cancellationToken);

    public async Task<(IReadOnlyList<Customer> Items, int TotalCount)> ListAsync(
        Guid lenderId, string? search, bool? isActive, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = db.Customers.AsNoTracking().Where(x => x.LenderId == lenderId);

        if (isActive is not null)
        {
            query = query.Where(x => x.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.ToLower();
            query = query.Where(x => x.FullName.ToLower().Contains(term) || x.PhoneNumber.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.FullName).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public void Add(Customer customer) => db.Customers.Add(customer);

    public async Task<IReadOnlyList<Loan>> GetLoansAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        await db.Loans.AsNoTracking()
            .Where(x => x.CustomerId == customerId)
            .OrderByDescending(x => x.StartDate).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Loan>> GetOpenLoansAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        await db.Loans.AsNoTracking()
            .Where(l => l.CustomerId == customerId && (l.Status == LoanStatus.Active || l.Status == LoanStatus.Overdue))
            .ToListAsync(cancellationToken);
}
