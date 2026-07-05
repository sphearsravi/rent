namespace RentCollection.Core.Entities;

public class Company
{
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? TaxId { get; set; }
    public string? LogoUrl { get; set; }
    public string SubscriptionType { get; set; } = "Basic";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedDate { get; set; }

    // Navigation properties
    public ICollection<Property> Properties { get; set; } = new List<Property>();
    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
    public ICollection<Tenant> Tenants { get; set; } = new List<Tenant>();
}
