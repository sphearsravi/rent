namespace RentCollection.Application.DTOs;

public class CashPaymentDto
{
    public int? InvoiceId { get; set; }
    public int TenantId { get; set; }
    public int PropertyId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public string? ReceiptNumber { get; set; }
    public string? Notes { get; set; }
}

public class CashPaymentResponseDto
{
    public int PaymentId { get; set; }
    public int CashPaymentId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
}
