namespace RentCollection.Core.Entities;

public class MaintenanceRequest
{
    public int RequestId { get; set; }
    public int PropertyId { get; set; }
    public int? UnitId { get; set; }
    public int? TenantId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Priority { get; set; } = "Medium";
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public string? AssignedToUserId { get; set; }
    public int? AssignedVendorId { get; set; }              // Item 7 — external contractor
    public DateTime RequestDate { get; set; } = DateTime.UtcNow;
    public DateTime? DueDate { get; set; }                  // Item 7 — SLA clock; set from Priority at create-time
    public DateTime? CompletedDate { get; set; }
    public decimal? EstimatedCost { get; set; }
    public decimal? ActualCost { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedDate { get; set; }

    // Navigation properties
    public Property Property { get; set; } = null!;
    public Unit? Unit { get; set; }
    public Tenant? Tenant { get; set; }
    public ApplicationUser? AssignedToUser { get; set; }
    public Vendor? AssignedVendor { get; set; }
    public ICollection<MaintenancePhoto> Photos { get; set; } = new List<MaintenancePhoto>();

    /// <summary>True when DueDate has passed and the request is still open.</summary>
    public bool IsOverdue => DueDate.HasValue && DateTime.UtcNow > DueDate.Value
        && Status != "Completed" && Status != "Cancelled";

    /// <summary>
    /// SLA hours by priority — used by the controller when stamping DueDate
    /// at create-time. Tune per company in a future iteration.
    /// </summary>
    public static int SlaHoursForPriority(string priority) => priority switch
    {
        "Emergency" => 4,
        "High" => 24,
        "Medium" => 72,
        "Low" => 168,
        _ => 72
    };
}
