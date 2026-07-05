using RentCollection.Core.Enums;

namespace RentCollection.Core.Entities;

public class BankTransfer
{
    public int BankTransferId { get; set; }
    public int PaymentId { get; set; }
    public int TenantId { get; set; }
    public int PropertyId { get; set; }
    public int? InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string BankName { get; set; } = string.Empty;
    public DateTime TransferDate { get; set; }
    public string? ReferenceNumber { get; set; }
    public string ProofFilePath { get; set; } = string.Empty; // Path to uploaded transfer slip
    public string ProofFileName { get; set; } = string.Empty;
    public string? ProofFileType { get; set; }
    public long? ProofFileSize { get; set; }
    public BankTransferStatus Status { get; set; } = BankTransferStatus.PendingVerification;
    public string? Notes { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedDate { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime SubmittedDate { get; set; } = DateTime.UtcNow;
    public string SubmittedBy { get; set; } = string.Empty;

    // Navigation properties
    public Payment Payment { get; set; } = null!;
    public Tenant Tenant { get; set; } = null!;
    public Property Property { get; set; } = null!;
    public Invoice? Invoice { get; set; }
}
