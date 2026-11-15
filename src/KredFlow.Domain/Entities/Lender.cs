namespace KredFlow.Domain.Entities;

public class Lender
{
    public const string DefaultTimeZone = "Asia/Kolkata";

    public Guid Id { get; set; }
    public string BusinessName { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string TimeZone { get; set; } = DefaultTimeZone;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
    public ICollection<Customer> Customers { get; set; } = new List<Customer>();
    public ICollection<Loan> Loans { get; set; } = new List<Loan>();
}
