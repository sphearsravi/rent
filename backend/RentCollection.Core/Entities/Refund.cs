namespace RentCollection.Core.Entities;

/// <summary>
/// A refund issued against a prior Payment. Records the financial reversal
/// and the operational reason so reports can trace customer-owed money.
/// Refunds always tie back to a specific Payment (full or partial); a refund
/// without a payment is just a credit note.
/// </summary>
public class Refund
{
    public int RefundId { get; set; }
    public int PaymentId { get; set; }
    public Payment Payment { get; set; } = null!;

    public decimal Amount { get; set; }
    public string Method { get; set; } = "BankTransfer";   // BankTransfer/Cash/KNETReversal
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = RefundStatus.Pending; // Pending/Approved/Settled/Rejected
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? ApprovedDate { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? SettledDate { get; set; }
}

public static class RefundStatus
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Settled = "Settled";
    public const string Rejected = "Rejected";
}
