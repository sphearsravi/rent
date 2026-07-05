namespace RentCollection.Web.DTOs.Api;

// Login Request
public class LoginRequest
{
    public LoginInput? Input { get; set; }
    public LoginParam? Param { get; set; }
}

public class LoginInput
{
    public string? Username { get; set; }
    public string? Password { get; set; }
}

// Login Response
public class LoginResponseData
{
    public int TenantId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public string? PACINumber { get; set; }
    public int? CompanyId { get; set; }
    public string? Token { get; set; }
    public DateTime? CreatedDate { get; set; }
}
