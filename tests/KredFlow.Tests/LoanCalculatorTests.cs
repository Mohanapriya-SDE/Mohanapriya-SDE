using KredFlow.Application.Exceptions;
using KredFlow.Application.Loans;
using KredFlow.Domain.Enums;

namespace KredFlow.Tests;

public sealed class LoanCalculatorTests
{
    [Fact]
    public void CalculateEmi_MatchesReducingBalanceFormula()
    {
        Assert.Equal(8884.88m, LoanCalculator.CalculateEmi(100_000m, 12m, 12));
    }

    [Fact]
    public void CalculateEmi_ZeroRate_SplitsPrincipalEvenly()
    {
        Assert.Equal(1000m, LoanCalculator.CalculateEmi(12_000m, 0m, 12));
    }

    [Theory]
    [InlineData(100_000, 12, 12)]
    [InlineData(1_000, 0.01, 1)]
    [InlineData(250_000.55, 18.5, 36)]
    [InlineData(10_000_000, 60, 360)]
    [InlineData(99_999.99, 7.35, 7)]
    public void GenerateSchedule_ClearsExactlyThePrincipalOverTheTenure(double principal, double rate, int months)
    {
        var p = (decimal)principal;
        var emi = LoanCalculator.CalculateEmi(p, (decimal)rate, months);

        var rows = LoanCalculator.GenerateSchedule(p, (decimal)rate, emi, months, new DateOnly(2026, 1, 15));

        Assert.Equal(months, rows.Count);
        Assert.Equal(p, rows.Sum(r => r.PrincipalDue));
        Assert.All(rows, r => Assert.True(r.PrincipalDue > 0));
        Assert.Equal(p, rows[0].OpeningPrincipal);
        Assert.Equal(Enumerable.Range(1, months), rows.Select(r => r.InstallmentNumber));
        for (var i = 1; i < rows.Count; i++)
        {
            Assert.Equal(rows[i - 1].OpeningPrincipal - rows[i - 1].PrincipalDue, rows[i].OpeningPrincipal);
        }
    }

    [Fact]
    public void GenerateSchedule_FirstRowSplitsInterestAndPrincipal()
    {
        var rows = LoanCalculator.GenerateSchedule(100_000m, 12m, 8884.88m, 12, new DateOnly(2026, 11, 15));

        Assert.Equal(1000m, rows[0].InterestDue);
        Assert.Equal(7884.88m, rows[0].PrincipalDue);
        Assert.Equal(8884.88m, rows[0].TotalDue);
        Assert.Equal(new DateOnly(2026, 12, 15), rows[0].DueDate);
    }

    [Fact]
    public void GenerateSchedule_DueDatesClampToMonthEndWithoutDrifting()
    {
        var rows = LoanCalculator.GenerateSchedule(10_000m, 12m, 3_400m, 3, new DateOnly(2026, 1, 31));

        Assert.Equal(
            [new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 30)],
            rows.Select(r => r.DueDate));
    }

    [Fact]
    public void GenerateSchedule_ContinuesNumberingAndClearsEarlierWithLargerEmi()
    {
        var rows = LoanCalculator.GenerateSchedule(50_000m, 12m, 8884.88m, 12, new DateOnly(2026, 1, 15), firstInstallmentNumber: 4);

        Assert.True(rows.Count < 12);
        Assert.Equal(4, rows[0].InstallmentNumber);
        Assert.Equal(new DateOnly(2026, 5, 15), rows[0].DueDate);
        Assert.Equal(50_000m, rows.Sum(r => r.PrincipalDue));
    }

    private static ScheduleRowFigures Row(int n, string due, decimal principal, decimal interest, decimal paidPrincipal = 0, decimal paidInterest = 0) =>
        new(Guid.NewGuid(), n, DateOnly.Parse(due), 0m, principal, interest, paidPrincipal, paidInterest);

    [Fact]
    public void PlanPrepayment_RejectsWhenAnInstallmentAlreadyDueIsUnpaid()
    {
        var rows = new[] { Row(1, "2026-01-15", 800, 100), Row(2, "2026-02-15", 810, 90) };

        var ex = Assert.Throws<BusinessRuleException>(() =>
            PaymentAllocationEngine.PlanPrepayment(rows, 1610m, 500m, new DateOnly(2026, 1, 20)));

        Assert.Contains("must be settled", ex.Message);
    }

    [Fact]
    public void PlanPrepayment_RejectsMoreThanThePayoffAmount()
    {
        var rows = new[] { Row(1, "2026-01-15", 800, 100, 800, 100), Row(2, "2026-02-15", 810, 90) };

        Assert.Throws<BusinessRuleException>(() =>
            PaymentAllocationEngine.PlanPrepayment(rows, 810m, 810.01m, new DateOnly(2026, 1, 20)));
    }

    [Fact]
    public void PlanPrepayment_SettlesPartlyPaidFutureInstallmentBeforePrincipal()
    {
        var rows = new[]
        {
            Row(1, "2026-01-15", 800, 100, 800, 100),
            Row(2, "2026-02-15", 810, 90, paidInterest: 90, paidPrincipal: 10),
            Row(3, "2026-03-15", 820, 80)
        };

        var plan = PaymentAllocationEngine.PlanPrepayment(rows, 1630m, 1_000m, new DateOnly(2026, 1, 20));

        Assert.Equal(800m, plan.Lines[0].Principal);
        Assert.Equal(rows[1].Id, plan.Lines[0].ScheduleId);
        Assert.Equal(AllocationType.PrincipalPrepayment, plan.Lines[1].Type);
        Assert.Null(plan.Lines[1].ScheduleId);
        Assert.Equal(200m, plan.PrincipalPrepaid);
        Assert.Equal(630m, plan.NewPrincipalBalance);
        Assert.Equal(2, plan.LastKeptInstallmentNumber);
        Assert.Equal([rows[2].Id], plan.ReplacedRowIds);
    }

    [Fact]
    public void BalanceCalculator_AccruesInterestOnlyOnInstallmentsAlreadyDue()
    {
        var rows = new[]
        {
            Row(1, "2026-01-15", 800, 100, 800, 100),
            Row(2, "2026-02-15", 810, 90),
            Row(3, "2026-03-15", 820, 80)
        };

        var onDueDate = LoanBalanceCalculator.Calculate(2430m, 800m, rows, new DateOnly(2026, 2, 15));
        Assert.Equal(1630m, onDueDate.PrincipalOutstanding);
        Assert.Equal(90m, onDueDate.AccruedUnpaidInterest);
        Assert.Equal(0m, onDueDate.OverdueAmount);
        Assert.Equal(1720m, onDueDate.TotalOutstanding);

        var later = LoanBalanceCalculator.Calculate(2430m, 800m, rows, new DateOnly(2026, 2, 16));
        Assert.Equal(900m, later.OverdueAmount);
    }
}
