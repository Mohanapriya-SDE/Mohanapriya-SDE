namespace KredFlow.Domain.Entities;

public class Lender
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public ICollection<Customer> Customers { get; set; } = new List<Customer>();
    public ICollection<Loan> Loans { get; set; } = new List<Loan>();
}
