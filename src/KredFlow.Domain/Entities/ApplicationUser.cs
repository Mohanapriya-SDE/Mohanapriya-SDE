using Microsoft.AspNetCore.Identity;

namespace KredFlow.Domain.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public Guid LenderId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public Lender Lender { get; set; } = null!;
}
