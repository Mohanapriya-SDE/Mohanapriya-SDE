using System.Text.Json.Serialization;
using KredFlow.Domain.Enums;

namespace KredFlow.Application.Payments;

public enum PaymentStatus
{
    Recorded,
    Reversed
}

public class CreatePaymentRequest
{
    public decimal Amount { get; set; }
    public DateOnly PaymentDate { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PaymentMethod PaymentMethod { get; set; }

    public string? ReferenceNumber { get; set; }
    public string? Remarks { get; set; }

    /// <summary>Retrying a request with the same key returns the original payment instead of posting it twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ReversePaymentRequest
{
    public string Reason { get; set; } = string.Empty;
    public DateOnly ReversalDate { get; set; }
}

public class PaymentListQuery
{
    public const int MaxPageSize = 100;

    public PaymentStatus? Status { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class PaymentSummaryResponse
{
    public Guid Id { get; set; }
    public Guid LoanId { get; set; }
    public Guid CustomerId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly PaymentDate { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PaymentMethod PaymentMethod { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PaymentType Type { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PaymentStatus Status { get; set; }

    public string? ReferenceNumber { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PaymentAllocationResponse
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AllocationType AllocationType { get; set; }

    public int? InstallmentNumber { get; set; }
    public Guid? PenaltyId { get; set; }
    public decimal InterestAmount { get; set; }
    public decimal PenaltyAmount { get; set; }
    public decimal PrincipalAmount { get; set; }
}

public class PaymentReversalResponse
{
    public string Reason { get; set; } = string.Empty;
    public DateOnly ReversalDate { get; set; }
    public DateTime ReversedAt { get; set; }
}

public class PaymentResponse : PaymentSummaryResponse
{
    public decimal InterestAllocated { get; set; }
    public decimal PenaltyAllocated { get; set; }
    public decimal PrincipalAllocated { get; set; }
    public List<PaymentAllocationResponse> Allocations { get; set; } = [];
    public PaymentReversalResponse? Reversal { get; set; }
}

/// <summary>The outcome of recording a payment. Replayed is true when an earlier request with the same key was returned.</summary>
public sealed record RecordPaymentResult(PaymentResponse Payment, bool Replayed);
