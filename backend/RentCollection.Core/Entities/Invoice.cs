using System.ComponentModel.DataAnnotations;

namespace RentCollection.Core.Entities;

public class Invoice
{
    public int InvoiceId { get; set; }
    public int LeaseId { get; set; }
    public int TenantId { get; set; }
    public DateTime InvoiceDate { get; set; }
    public DateTime DueDate { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public string Status { get; set; } = "Pending";
    public string InvoiceType { get; set; } = "Rent";
    public string? Notes { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    // Optimistic concurrency token to prevent lost-update races on PaidAmount/Status
    // when callbacks and retries land at the same time.
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    // Navigation properties
    public Lease Lease { get; set; } = null!;
    public Tenant Tenant { get; set; } = null!;
}
