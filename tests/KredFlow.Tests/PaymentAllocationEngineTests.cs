using KredFlow.Application.Exceptions;
using KredFlow.Application.Loans;
using KredFlow.Domain.Enums;

namespace KredFlow.Tests;

public sealed class PaymentAllocationEngineTests
{
    private static ScheduleRowFigures Row(int n, string due, decimal principal, decimal interest, decimal paidPrincipal = 0, decimal paidInterest = 0) =>
        new(Guid.NewGuid(), n, DateOnly.Parse(due), 0m, principal, interest, paidPrincipal, paidInterest);

    private static PenaltyFigures Penalty(string assessed, decimal amount, decimal waived = 0, decimal paid = 0) =>
        new(Guid.NewGuid(), null, DateOnly.Parse(assessed), amount, waived, paid);

    private static readonly DateOnly PayDate = new(2026, 3, 20);

    [Fact]
    public void AllocatePayment_FollowsInterestThenPenaltyThenPrincipalAcrossDueInstallments()
    {
        var rows = new[]
        {
            Row(1, "2026-02-15", 800, 100),
            Row(2, "2026-03-15", 810, 90),
            Row(3, "2026-04-15", 820, 80)
        };
        var penalties = new[] { Penalty("2026-03-01", 50) };

        // 100 + 90 interest due, 50 penalty, then 260 of principal, which all goes to the oldest installment.
        var lines = PaymentAllocationEngine.AllocatePayment(rows, penalties, 100 + 90 + 50 + 260, PayDate);

        Assert.Equal(3, lines.Count);
        Assert.Equal((100m, 260m), (lines[0].Interest, lines[0].Principal));
        Assert.Equal(rows[0].Id, lines[0].ScheduleId);
        Assert.Equal((90m, 0m), (lines[1].Interest, lines[1].Principal));
        Assert.Equal(AllocationType.Penalty, lines[2].Type);
        Assert.Equal(50m, lines[2].Penalty);
        Assert.Equal(penalties[0].Id, lines[2].PenaltyId);
        Assert.Equal(500m, lines.Sum(l => l.Total));
    }

    [Fact]
    public void AllocatePayment_SettlesOldestPenaltyFirstAndSupportsPartialPenaltyPayment()
    {
        var older = Penalty("2026-01-01", 100);
        var newer = Penalty("2026-02-01", 100);

        var lines = PaymentAllocationEngine.AllocatePayment([], [newer, older], 150, PayDate);

        Assert.Equal([older.Id, newer.Id], lines.Select(l => l.PenaltyId!.Value));
        Assert.Equal([100m, 50m], lines.Select(l => l.Penalty));
    }

    [Fact]
    public void AllocatePayment_IgnoresWaivedAndPaidPenaltyAmounts()
    {
        var penalty = Penalty("2026-01-01", 100, waived: 30, paid: 20);

        var lines = PaymentAllocationEngine.AllocatePayment([], [penalty], 50, PayDate);

        Assert.Equal(50m, Assert.Single(lines).Penalty);
        Assert.Throws<BusinessRuleException>(() => PaymentAllocationEngine.AllocatePayment([], [penalty], 50.01m, PayDate));
    }

    [Fact]
    public void AllocatePayment_RemainderAdvancesUpcomingInstallmentsInterestFirst()
    {
        var rows = new[]
        {
            Row(1, "2026-03-15", 800, 100, 800, 100),
            Row(2, "2026-04-15", 810, 90),
            Row(3, "2026-05-15", 820, 80)
        };

        var lines = PaymentAllocationEngine.AllocatePayment(rows, [], 90 + 810 + 30, PayDate);

        Assert.Equal(2, lines.Count);
        Assert.Equal((90m, 810m), (lines[0].Interest, lines[0].Principal));
        Assert.Equal((30m, 0m), (lines[1].Interest, lines[1].Principal));
        Assert.Equal(rows[2].Id, lines[1].ScheduleId);
    }

    [Fact]
    public void AllocatePayment_CompletesPartlyPaidDueInstallmentsBeforeAnythingElse()
    {
        var rows = new[]
        {
            Row(1, "2026-02-15", 800, 100, paidInterest: 100, paidPrincipal: 300),
            Row(2, "2026-03-15", 810, 90)
        };

        var lines = PaymentAllocationEngine.AllocatePayment(rows, [], 90 + 500, PayDate);

        Assert.Equal((0m, 500m), (lines[0].Interest, lines[0].Principal));
        Assert.Equal(rows[0].Id, lines[0].ScheduleId);
        Assert.Equal((90m, 0m), (lines[1].Interest, lines[1].Principal));
    }

    [Fact]
    public void AllocatePayment_RejectsMoreThanEverythingStillPayable()
    {
        var rows = new[] { Row(1, "2026-02-15", 800, 100) };
        var penalties = new[] { Penalty("2026-02-20", 50) };

        Assert.Equal(950m, PaymentAllocationEngine.AllocatePayment(rows, penalties, 950, PayDate).Sum(l => l.Total));
        var ex = Assert.Throws<BusinessRuleException>(() => PaymentAllocationEngine.AllocatePayment(rows, penalties, 950.01m, PayDate));
        Assert.Contains("950.00", ex.Message);
    }

    [Fact]
    public void AllocatePayment_NeverAllocatesToFutureInstallmentsWhileDuesRemain()
    {
        var rows = new[] { Row(1, "2026-02-15", 800, 100), Row(2, "2026-04-15", 810, 90) };

        var lines = PaymentAllocationEngine.AllocatePayment(rows, [], 500, PayDate);

        Assert.Equal(rows[0].Id, Assert.Single(lines).ScheduleId);
    }

    [Fact]
    public void PlanPrepayment_RejectsWhilePenaltiesAreOutstanding()
    {
        var rows = new[] { Row(1, "2026-04-15", 800, 100) };

        var ex = Assert.Throws<BusinessRuleException>(() =>
            PaymentAllocationEngine.PlanPrepayment(rows, 800m, 500m, PayDate, outstandingPenalties: 25m));

        Assert.Contains("Penalties", ex.Message);
    }
}
