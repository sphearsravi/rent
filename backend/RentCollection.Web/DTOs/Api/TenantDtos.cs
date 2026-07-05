namespace RentCollection.Web.DTOs.Api;

// Get Info - GET Tenant Information
public class GetInfoRequest
{
    public GetInfoInput? Input { get; set; }
    public LoginParam? Param { get; set; }
}

public class GetInfoInput
{
    public int Tenantid { get; set; }
    public string? Fcmtoken { get; set; }
}

// Get Property By Tenant ID
public class GetPropertyByTenantIdRequest
{
    public GetPropertyByTenantIdInput? Input { get; set; }
    public LoginParam? Param { get; set; }
}

public class GetPropertyByTenantIdInput
{
    public int Tenantid { get; set; }
}

// Get Pending Rent Data
public class GetPendingRentDataRequest
{
    public GetPendingRentDataInput? Input { get; set; }
    public LoginParam? Param { get; set; }
}

public class GetPendingRentDataInput
{
    public int Tenantid { get; set; }
}

// Get Paid Rent Data
public class GetPaidRentDataRequest
{
    public GetPaidRentDataInput? Input { get; set; }
    public LoginParam? Param { get; set; }
}

public class GetPaidRentDataInput
{
    public int Tenantid { get; set; }
    public int Lastid { get; set; }
    public int Count { get; set; }
}

// Get Receipt
public class GetReceiptDataRequest
{
    public InputData? Input { get; set; }
    public ParamData? Param { get; set; }
}

public class InputData
{
    public string? TrackId { get; set; }
    public int TenantId { get; set; }
}

// Change Password
public class ChangePasswordRequest
{
    public ChangePasswordInput? Input { get; set; }
    public LoginParam? Param { get; set; }
}

public class ChangePasswordInput
{
    public int Tenantid { get; set; }
    public string? Oldpassword { get; set; }
    public string? Newpassword { get; set; }
}

// Forgot Password
public class ForgotPasswordRequest
{
    public ForgotPasswordInput? Input { get; set; }
    public LoginParam? Param { get; set; }
}

public class ForgotPasswordInput
{
    public string? Civilid { get; set; }
}

// Response Data Models
public class TenantInfoData
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
    public DateTime? CreatedDate { get; set; }
}

public class PropertyData
{
    public int PropertyId { get; set; }
    public string? PropertyName { get; set; }
    public string? PropertyNumber { get; set; }
    public string? BuildingName { get; set; }
    public string? Area { get; set; }
    public string? Block { get; set; }
    public string? Street { get; set; }
    public string? Address { get; set; }
}

public class PendingRentData
{
    public int TenantRentHistoryId { get; set; }
    public int TenantId { get; set; }
    public int PropertyId { get; set; }
    public string? PropertyName { get; set; }
    public string? PropertyNumber { get; set; }
    public DateTime? RentDate { get; set; }
    public decimal? RentAmount { get; set; }
    public decimal? NetAmount { get; set; }
    public string? Currency { get; set; }
    public string? Status { get; set; }
}

public class PaidRentData
{
    public int TenantRentHistoryId { get; set; }
    public int TenantId { get; set; }
    public int PropertyId { get; set; }
    public string? PropertyName { get; set; }
    public string? PropertyNumber { get; set; }
    public DateTime? RentDate { get; set; }
    public DateTime? PaidDate { get; set; }
    public decimal? RentAmount { get; set; }
    public decimal? PaidAmount { get; set; }
    public string? Currency { get; set; }
    public string? PaymentReference { get; set; }
    public string? PaymentId { get; set; }
}

public class ReceiptData
{
    public int PaymentId { get; set; }
    public int TenantId { get; set; }
    public string? TenantName { get; set; }
    public string? PropertyName { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTime? PaymentDate { get; set; }
    public string? PaymentReference { get; set; }
    public string? TrackId { get; set; }
}
