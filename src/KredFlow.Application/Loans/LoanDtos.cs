using System.Text.Json.Serialization;
using KredFlow.Domain.Enums;

namespace KredFlow.Application.Loans;

/// <summary>The EMI, lender and schedule rows are backend responsibilities and are deliberately not accepted here.</summary>
public class CreateLoanRequest
{
    public Guid CustomerId { get; set; }
    public decimal PrincipalAmount { get; set; }
    public decimal AnnualInterestRate { get; set; }
    public int TenureMonths { get; set; }
    public DateOnly StartDate { get; set; }

    /// <summary>Optional; defaults to <c>Loans:DefaultPrepaymentPolicy</c>.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PrepaymentPolicy? PrepaymentPolicy { get; set; }
}

public class LoanResponse
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public decimal PrincipalAmount { get; set; }
    public decimal AnnualInterestRate { get; set; }
    public int TenureMonths { get; set; }
    public decimal EmiAmount { get; set; }
    public DateOnly StartDate { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LoanStatus Status { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PrepaymentPolicy PrepaymentPolicy { get; set; }

    public bool ScheduleGenerated { get; set; }
    public DateOnly? ClosedDate { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class LoanListQuery
{
    public const int MaxPageSize = 100;

    public Guid? CustomerId { get; set; }
    public LoanStatus? Status { get; set; }
    public decimal? MinPrincipal { get; set; }
    public decimal? MaxPrincipal { get; set; }
    public DateOnly? StartDateFrom { get; set; }
    public DateOnly? StartDateTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class RepaymentScheduleResponse
{
    public int InstallmentNumber { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal OpeningPrincipal { get; set; }
    public decimal PrincipalDue { get; set; }
    public decimal InterestDue { get; set; }
    public decimal TotalDue { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SchedulePaymentStatus PaymentStatus { get; set; }
}

public class LoanOutstandingResponse
{
    public Guid LoanId { get; set; }
    public decimal PrincipalOutstanding { get; set; }
    public decimal AccruedUnpaidInterest { get; set; }
    public decimal OverdueAmount { get; set; }
    public decimal TotalOutstanding { get; set; }
    public DateOnly AsOfDate { get; set; }
}

public class CloseLoanRequest
{
    public DateOnly ClosureDate { get; set; }
    public string? Remarks { get; set; }
}

public class PrepaymentRequest
{
    public decimal Amount { get; set; }
    public DateOnly PaymentDate { get; set; }

    /// <summary>Required when the loan's policy is LenderChoice; otherwise omitted or equal to the policy.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PrepaymentTreatment? Treatment { get; set; }

    /// <summary>Defaults to Cash.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PaymentMethod? PaymentMethod { get; set; }

    public string? Remarks { get; set; }
}

public class PrepaymentResponse
{
    public Guid PaymentId { get; set; }
    public Guid LoanId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly PaymentDate { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PrepaymentTreatment Treatment { get; set; }

    /// <summary>Settled on partly paid installments before the rest went to principal.</summary>
    public decimal InstallmentAmountSettled { get; set; }
    public decimal PrincipalPrepaid { get; set; }
    public decimal PrincipalOutstanding { get; set; }
    public decimal EmiAmount { get; set; }
    public int TenureMonths { get; set; }
}
