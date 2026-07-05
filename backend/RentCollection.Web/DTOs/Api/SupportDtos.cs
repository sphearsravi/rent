namespace RentCollection.Web.DTOs.Api;

// Tenant Input for GetMaintenanceCategories
public class TenantInput
{
    public int Tenantid { get; set; }
}

// Get Maintenance Categories Request
public class GetMaintenanceCategoriesRequest
{
    public TenantInput? Input { get; set; }
    public TenantParam? Param { get; set; }
}

// Save Support Ticket Request
public class SaveSupportTicketRequest
{
    public SaveSupportTicketInput? Input { get; set; }
    public LoginParam? Param { get; set; }
}

public class SaveSupportTicketInput
{
    public int Tenantid { get; set; }
    public int Maintcategoryid { get; set; }
    public int Propertyid { get; set; }
    public int Propertyunitid { get; set; }
    public string? Notes { get; set; }
    public List<string>? Images { get; set; }
}

// Response Data Models
public class MaintenanceCategoryData
{
    public int CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? CategoryNameArabic { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
}

public class SupportTicketResponseData
{
    public int TicketId { get; set; }
    public string? TicketNumber { get; set; }
    public string? Status { get; set; }
    public DateTime? CreatedDate { get; set; }
    public string? Message { get; set; }
}
