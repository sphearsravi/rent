using System.ComponentModel.DataAnnotations;

namespace RentCollection.Web.Models.ViewModels;

public class UserViewModel
{
    public string Id { get; set; } = string.Empty;

    [Required(ErrorMessage = "First name is required")]
    [StringLength(50, ErrorMessage = "First name cannot exceed 50 characters")]
    [Display(Name = "First Name")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last name is required")]
    [StringLength(50, ErrorMessage = "Last name cannot exceed 50 characters")]
    [Display(Name = "Last Name")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Invalid phone number")]
    [Display(Name = "Phone Number")]
    public string? PhoneNumber { get; set; }

    [Display(Name = "Company")]
    public int? CompanyId { get; set; }

    [Display(Name = "Company Name")]
    public string? CompanyName { get; set; }

    [Display(Name = "Is Active")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Email Verified")]
    public bool EmailVerified { get; set; }

    [Display(Name = "Roles")]
    public List<string> Roles { get; set; } = new List<string>();

    [Display(Name = "Selected Roles")]
    public List<string> SelectedRoles { get; set; } = new List<string>();

    [Display(Name = "Created Date")]
    public DateTime CreatedDate { get; set; }

    [Display(Name = "Last Login")]
    public DateTime? LastLogin { get; set; }

    // For password (only used during creation)
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters long")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$",
        ErrorMessage = "Password must contain at least one uppercase letter, one lowercase letter, one number and one special character")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string? Password { get; set; }

    [DataType(DataType.Password)]
    [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
    [Display(Name = "Confirm Password")]
    public string? ConfirmPassword { get; set; }
}
