namespace KredFlow.Application.Penalties;

public class AssessPenaltyRequest
{
    public decimal Amount { get; set; }
    public DateOnly AssessedDate { get; set; }

    /// <summary>Optional: ties the penalty to one installment of the loan's current schedule.</summary>
    public int? InstallmentNumber { get; set; }
}

public class WaivePenaltyRequest
{
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class PenaltyResponse
{
    public Guid Id { get; set; }
    public Guid LoanId { get; set; }
    public int? InstallmentNumber { get; set; }
    public DateOnly AssessedDate { get; set; }
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal WaivedAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public DateTime CreatedAt { get; set; }
}
