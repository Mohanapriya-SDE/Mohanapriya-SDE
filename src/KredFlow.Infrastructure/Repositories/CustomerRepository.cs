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

    public async Task<(int OpenLoanCount, decimal TotalOutstanding)> GetOutstandingAsync(
        Guid customerId, CancellationToken cancellationToken = default)
    {
        var open = await db.Loans.AsNoTracking()
            .Where(l => l.CustomerId == customerId && (l.Status == LoanStatus.Active || l.Status == LoanStatus.Overdue))
            .Select(l => new { l.Id, l.Principal })
            .ToListAsync(cancellationToken);

        if (open.Count == 0)
        {
            return (0, 0m);
        }

        var loanIds = open.Select(l => l.Id).ToList();

        // Summed client-side because Sqlite (used in tests) can't aggregate decimals in SQL.
        var due = await db.RepaymentSchedules.AsNoTracking()
            .Where(s => loanIds.Contains(s.LoanId))
            .Select(s => new { s.LoanId, s.TotalDue })
            .ToListAsync(cancellationToken);

        var paid = await db.PaymentAllocations.AsNoTracking()
            .Where(a => loanIds.Contains(a.Schedule.LoanId))
            .Select(a => new { a.Schedule.LoanId, a.InterestAmount, a.PrincipalAmount })
            .ToListAsync(cancellationToken);

        var dueByLoan = due.GroupBy(x => x.LoanId).ToDictionary(g => g.Key, g => g.Sum(x => x.TotalDue));
        var paidByLoan = paid.GroupBy(x => x.LoanId).ToDictionary(g => g.Key, g => g.Sum(x => x.InterestAmount + x.PrincipalAmount));

        // A loan with no schedule yet is still owed its principal.
        var total = open.Sum(l => dueByLoan.TryGetValue(l.Id, out var d)
            ? Math.Max(0m, d - paidByLoan.GetValueOrDefault(l.Id))
            : l.Principal);

        return (open.Count, total);
    }
}
