using KredFlow.Domain.Enums;

namespace KredFlow.Application.Loans;

public class LoanOptions
{
    public const string SectionName = "Loans";

    public decimal MinPrincipal { get; set; } = 1_000m;
    public decimal MaxPrincipal { get; set; } = 10_000_000m;
    public decimal MinAnnualInterestRate { get; set; } = 0.01m;
    public decimal MaxAnnualInterestRate { get; set; } = 60m;
    public int MinTenureMonths { get; set; } = 1;
    public int MaxTenureMonths { get; set; } = 360;

    /// <summary>How far before the lender's today a loan may start (for loans being migrated in).</summary>
    public int MaxBackdatedDays { get; set; } = 365;
    public int MaxFutureDays { get; set; } = 30;

    /// <summary>Applied when a loan is created without an explicit policy.</summary>
    public PrepaymentPolicy DefaultPrepaymentPolicy { get; set; } = PrepaymentPolicy.LenderChoice;

    /// <summary>Whether outstanding penalties stop a loan from being closed.</summary>
    public PenaltyClosurePolicy PenaltyClosurePolicy { get; set; } = PenaltyClosurePolicy.BlockUntilPaidOrWaived;
}

public enum PenaltyClosurePolicy
{
    /// <summary>A loan with an outstanding penalty cannot be closed.</summary>
    BlockUntilPaidOrWaived,

    /// <summary>The loan can close; its penalties, waivers and ledger entries stay as they are and the balance remains visible.</summary>
    IgnorePenalties
}
