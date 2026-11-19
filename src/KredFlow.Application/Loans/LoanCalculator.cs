namespace KredFlow.Application.Loans;

public sealed record ScheduleRow(
    int InstallmentNumber,
    DateOnly DueDate,
    decimal OpeningPrincipal,
    decimal PrincipalDue,
    decimal InterestDue)
{
    public decimal TotalDue => PrincipalDue + InterestDue;
}

/// <summary>Reducing-balance EMI and schedule maths. Pure and deterministic; money is rounded half away from zero to 2 places.</summary>
public static class LoanCalculator
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal CalculateEmi(decimal principal, decimal annualRatePercent, int months)
    {
        if (principal <= 0 || months <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(principal), "Principal and months must be positive.");
        }

        if (annualRatePercent <= 0)
        {
            return Round(principal / months);
        }

        var r = annualRatePercent / 1200m;
        var factor = 1m;
        for (var i = 0; i < months; i++)
        {
            factor *= 1 + r;
        }

        return Round(principal * r * factor / (factor - 1));
    }

    /// <summary>
    /// Builds installments until the balance is cleared or <paramref name="maxInstallments"/> is reached.
    /// The last installment absorbs the rounding residue. Due dates are counted from the loan's original start date.
    /// </summary>
    public static List<ScheduleRow> GenerateSchedule(
        decimal balance,
        decimal annualRatePercent,
        decimal emi,
        int maxInstallments,
        DateOnly loanStartDate,
        int firstInstallmentNumber = 1)
    {
        var monthlyRate = annualRatePercent / 1200m;
        var rows = new List<ScheduleRow>();

        for (var i = 0; i < maxInstallments && balance > 0; i++)
        {
            var number = firstInstallmentNumber + i;
            var interest = Round(balance * monthlyRate);
            var isLast = i == maxInstallments - 1;
            var principalDue = isLast || emi - interest >= balance ? balance : emi - interest;

            rows.Add(new ScheduleRow(number, loanStartDate.AddMonths(number), balance, principalDue, interest));
            balance -= principalDue;
        }

        return rows;
    }
}
