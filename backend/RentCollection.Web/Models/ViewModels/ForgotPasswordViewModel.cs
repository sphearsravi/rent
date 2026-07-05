using System.ComponentModel.DataAnnotations;

namespace RentCollection.Web.Models.ViewModels;

public class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;
}
