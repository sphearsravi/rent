namespace RentCollection.Core.Entities;

public class CashPayment
{
    public int CashPaymentId { get; set; }
    public int PaymentId { get; set; }
    public int TenantId { get; set; }
    public int PropertyId { get; set; }
    public int? InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public string CollectedBy { get; set; } = string.Empty; // Admin user who collected the cash
    public string? ReceiptNumber { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;

    // Navigation properties
    public Payment Payment { get; set; } = null!;
    public Tenant Tenant { get; set; } = null!;
    public Property Property { get; set; } = null!;
    public Invoice? Invoice { get; set; }
}
