namespace KredFlow.Domain.Entities;

public class Customer
{
    public Guid Id { get; set; }
    public Guid LenderId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;

    public Lender Lender { get; set; } = null!;
    public ICollection<Loan> Loans { get; set; } = new List<Loan>();
}
