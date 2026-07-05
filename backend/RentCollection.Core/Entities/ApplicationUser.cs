using Microsoft.AspNetCore.Identity;

namespace RentCollection.Core.Entities;

public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public int? CompanyId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool EmailVerified { get; set; } = false;
    public bool MFAEnabled { get; set; } = false;
    public DateTime? LastLogin { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedDate { get; set; }

    // Navigation properties
    public Company? Company { get; set; }
    public ICollection<Property> ManagedProperties { get; set; } = new List<Property>();
}
