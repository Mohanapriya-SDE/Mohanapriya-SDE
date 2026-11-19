using KredFlow.Application.Auth;
using KredFlow.Application.Common;
using KredFlow.Application.Exceptions;
using KredFlow.Application.Interfaces;
using KredFlow.Application.Loans;
using KredFlow.Domain.Entities;

namespace KredFlow.Application.Customers;

public class CustomerService(
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUser,
    ILoanRepository loans,
    ILenderClock clock,
    TimeProvider timeProvider) : ICustomerService
{
    public async Task<CustomerResponse> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            LenderId = currentUser.LenderId,
            FullName = request.FullName.Trim(),
            PhoneNumber = NormalizePhone(request.PhoneNumber),
            Address = NormalizeAddress(request.Address),
            IsActive = true,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        };

        customers.Add(customer);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToResponse(customer);
    }

    public async Task<CustomerResponse> GetAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        ToResponse(await LoadAsync(customerId, cancellationToken));

    public async Task<PagedResponse<CustomerResponse>> ListAsync(CustomerListQuery query, CancellationToken cancellationToken = default)
    {
        var (items, total) = await customers.ListAsync(
            currentUser.LenderId, query.Search?.Trim(), query.IsActive, query.Page, query.PageSize, cancellationToken);

        return new PagedResponse<CustomerResponse>
        {
            Items = items.Select(ToResponse).ToList(),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = total
        };
    }

    public async Task<CustomerResponse> UpdateAsync(Guid customerId, UpdateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var customer = await LoadAsync(customerId, cancellationToken);

        customer.FullName = request.FullName.Trim();
        customer.PhoneNumber = NormalizePhone(request.PhoneNumber);
        customer.Address = NormalizeAddress(request.Address);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToResponse(customer);
    }

    // Soft toggle only: loans and payments stay untouched. Blocking new loans for inactive customers belongs to loan creation.
    public async Task UpdateStatusAsync(Guid customerId, UpdateCustomerStatusRequest request, CancellationToken cancellationToken = default)
    {
        var customer = await LoadAsync(customerId, cancellationToken);

        customer.IsActive = request.IsActive;
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerLoanResponse>> GetLoansAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var customer = await LoadAsync(customerId, cancellationToken);
        var loans = await customers.GetLoansAsync(customer.Id, cancellationToken);

        return loans.Select(l => new CustomerLoanResponse
        {
            Id = l.Id,
            Principal = l.Principal,
            AnnualInterestRate = l.AnnualInterestRate,
            TenureMonths = l.TenureMonths,
            EmiAmount = l.EmiAmount,
            StartDate = l.StartDate,
            Status = l.Status
        }).ToList();
    }

    public async Task<CustomerOutstandingResponse> GetOutstandingAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var customer = await LoadAsync(customerId, cancellationToken);
        var openLoans = await customers.GetOpenLoansAsync(customer.Id, cancellationToken);
        var today = await clock.TodayAsync(cancellationToken);

        // Same definition as the loan endpoint: unpaid principal plus interest accrued on installments already due.
        var total = 0m;
        foreach (var loan in openLoans)
        {
            var rows = await loans.GetCurrentScheduleAsync(loan.Id, cancellationToken);
            var principalPaid = await loans.GetPrincipalPaidAsync(loan.Id, cancellationToken);
            total += LoanBalanceCalculator.Calculate(loan.Principal, principalPaid, rows.Select(ScheduleRowFigures.From), today).TotalOutstanding;
        }

        return new CustomerOutstandingResponse
        {
            CustomerId = customer.Id,
            OpenLoanCount = openLoans.Count,
            TotalOutstanding = total
        };
    }

    // A customer owned by another lender is reported as not found so ids can't be probed across lenders.
    private async Task<Customer> LoadAsync(Guid customerId, CancellationToken cancellationToken) =>
        await customers.GetByIdAsync(currentUser.LenderId, customerId, cancellationToken)
        ?? throw new NotFoundException("Customer not found.");

    private static string NormalizePhone(string value) =>
        PhoneNumberFormat.TryNormalize(value, out var normalized)
            ? normalized
            : throw new BusinessRuleException("Invalid phone number.");

    private static string? NormalizeAddress(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CustomerResponse ToResponse(Customer customer) => new()
    {
        Id = customer.Id,
        FullName = customer.FullName,
        PhoneNumber = customer.PhoneNumber,
        Address = customer.Address,
        IsActive = customer.IsActive,
        CreatedAt = DateTime.SpecifyKind(customer.CreatedAt, DateTimeKind.Utc)
    };
}
