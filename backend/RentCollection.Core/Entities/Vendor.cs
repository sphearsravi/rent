namespace RentCollection.Core.Entities;

/// <summary>
/// External contractor/vendor a maintenance request can be dispatched to.
/// Distinct from internal `User`/`ApplicationUser` so that companies can
/// maintain a vendor rolodex without inviting them as portal users.
/// </summary>
public class Vendor
{
    public int VendorId { get; set; }
    public int? CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }       // Plumbing/Electrical/HVAC/General/...
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public Company? Company { get; set; }
}
