using KredFlow.Domain.Entities;
using KredFlow.Domain.Enums;

namespace KredFlow.Application.Payments;

internal static class PaymentMapper
{
    /// <summary>Requires the payment's <c>Loan</c> and <c>Reversal</c> to be loaded.</summary>
    public static PaymentSummaryResponse ToSummary(Payment p) => Fill(new PaymentSummaryResponse(), p);

    /// <summary>Also requires allocations, with their schedule rows, to be loaded.</summary>
    public static PaymentResponse ToResponse(Payment p)
    {
        var response = Fill(new PaymentResponse(), p);

        response.Allocations = p.Allocations
            .OrderBy(a => a.Type == AllocationType.Installment ? 0 : a.Type == AllocationType.Penalty ? 1 : 2)
            .ThenBy(a => a.Schedule?.InstallmentNumber)
            .Select(a => new PaymentAllocationResponse
            {
                AllocationType = a.Type,
                InstallmentNumber = a.Schedule?.InstallmentNumber,
                PenaltyId = a.PenaltyId,
                InterestAmount = a.InterestAmount,
                PenaltyAmount = a.PenaltyAmount,
                PrincipalAmount = a.PrincipalAmount
            }).ToList();
        response.InterestAllocated = p.Allocations.Sum(a => a.InterestAmount);
        response.PenaltyAllocated = p.Allocations.Sum(a => a.PenaltyAmount);
        response.PrincipalAllocated = p.Allocations.Sum(a => a.PrincipalAmount);
        response.Reversal = p.Reversal is null
            ? null
            : new PaymentReversalResponse
            {
                Reason = p.Reversal.Reason,
                ReversalDate = p.Reversal.ReversalDate,
                ReversedAt = DateTime.SpecifyKind(p.Reversal.CreatedAt, DateTimeKind.Utc)
            };

        return response;
    }

    private static T Fill<T>(T response, Payment p) where T : PaymentSummaryResponse
    {
        response.Id = p.Id;
        response.LoanId = p.LoanId;
        response.CustomerId = p.Loan.CustomerId;
        response.Amount = p.Amount;
        response.PaymentDate = p.PaymentDate;
        response.PaymentMethod = p.PaymentMethod;
        response.Type = p.Type;
        response.Status = p.Reversal is null ? PaymentStatus.Recorded : PaymentStatus.Reversed;
        response.ReferenceNumber = p.ReferenceNumber;
        response.Remarks = p.Remarks;
        response.CreatedAt = DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc);
        return response;
    }
}
