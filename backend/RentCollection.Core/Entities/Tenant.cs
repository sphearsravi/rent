using System.ComponentModel.DataAnnotations;

namespace RentCollection.Core.Entities;

public class Tenant : ISoftDeletable
{
    public int TenantId { get; set; }

    [Required(ErrorMessage = "Company is required")]
    public int CompanyId { get; set; }

    [StringLength(100, ErrorMessage = "First name cannot exceed 100 characters")]
    public string? FirstName { get; set; }

    [StringLength(100, ErrorMessage = "Last name cannot exceed 100 characters")]
    public string? LastName { get; set; }

    // The tenant's Kuwait Civil ID — this IS the mobile-app login key (the create
    // flow copies it into GovernmentId, which tenant login matches on). Required and
    // exactly 12 digits so every tenant has a valid login.
    [Required(ErrorMessage = "Civil ID is required")]
    [RegularExpression(@"^\d{12}$", ErrorMessage = "Civil ID must be exactly 12 digits")]
    public string? PACINumber { get; set; }

    // Building PACI - links tenant to a property
    public string? BuildingPACINumber { get; set; }

    [StringLength(255, ErrorMessage = "Password cannot exceed 255 characters")]
    public string? Password { get; set; }

    /// <summary>
    /// Set to true when a system-generated temporary password is issued
    /// (initial onboarding or admin "Resend login"). The tenant portal
    /// blocks normal navigation and forces a password-change flow until
    /// the tenant picks their own password, at which point we clear it.
    /// </summary>
    public bool MustChangePasswordOnNextLogin { get; set; } = false;

    [EmailAddress(ErrorMessage = "Invalid email address")]
    [StringLength(255, ErrorMessage = "Email cannot exceed 255 characters")]
    public string? Email { get; set; }

    [Phone(ErrorMessage = "Invalid phone number")]
    [StringLength(20, ErrorMessage = "Phone cannot exceed 20 characters")]
    public string? Phone { get; set; }
    public DateTime? DateOfBirth { get; set; }
    [Display(Name = "Civil ID")]
    public string? GovernmentId { get; set; }
    public string? EmploymentStatus { get; set; }
    public string? EmergencyContact { get; set; }
    public string? EmergencyPhone { get; set; }
    public string? Status { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedDate { get; set; }

    // Navigation properties
    public Company? Company { get; set; }
    public ICollection<Lease> Leases { get; set; } = new List<Lease>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<MaintenanceRequest> MaintenanceRequests { get; set; } = new List<MaintenanceRequest>();

    // ISoftDeletable (Item 10)
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
