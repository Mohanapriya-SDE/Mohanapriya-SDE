using KredFlow.Domain.Enums;

namespace KredFlow.Domain.Entities;

public class Loan
{
    public Guid Id { get; set; }
    public Guid LenderId { get; set; }
    public Guid CustomerId { get; set; }
    public decimal Principal { get; set; }
    public decimal AnnualInterestRate { get; set; }
    public int TenureMonths { get; set; }
    public decimal EmiAmount { get; set; }
    public DateOnly StartDate { get; set; }
    public LoanStatus Status { get; set; }

    public Lender Lender { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public ICollection<RepaymentSchedule> RepaymentSchedules { get; set; } = new List<RepaymentSchedule>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<LoanTransaction> Transactions { get; set; } = new List<LoanTransaction>();
}
