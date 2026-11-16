using KredFlow.Domain.Enums;

namespace KredFlow.Application.Customers;

public class CreateCustomerRequest
{
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Address { get; set; }
}

public class UpdateCustomerRequest
{
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Address { get; set; }
}

public class UpdateCustomerStatusRequest
{
    public bool IsActive { get; set; }
}

public class CustomerListQuery
{
    public const int MaxPageSize = 100;

    /// <summary>Case-insensitive match on name or phone number.</summary>
    public string? Search { get; set; }
    public bool? IsActive { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class CustomerResponse
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Address { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CustomerLoanResponse
{
    public Guid Id { get; set; }
    public decimal Principal { get; set; }
    public decimal AnnualInterestRate { get; set; }
    public int TenureMonths { get; set; }
    public decimal EmiAmount { get; set; }
    public DateOnly StartDate { get; set; }
    public LoanStatus Status { get; set; }
}

public class CustomerOutstandingResponse
{
    public Guid CustomerId { get; set; }
    public int OpenLoanCount { get; set; }

    /// <summary>Unpaid schedule amounts across the customer's Active and Overdue loans.</summary>
    public decimal TotalOutstanding { get; set; }
}
