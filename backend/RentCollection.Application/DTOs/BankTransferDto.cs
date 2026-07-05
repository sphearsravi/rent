using Microsoft.AspNetCore.Http;

namespace RentCollection.Application.DTOs;

public class BankTransferSubmitDto
{
    public int? InvoiceId { get; set; }
    public int TenantId { get; set; }
    public int PropertyId { get; set; }
    public decimal Amount { get; set; }
    public DateTime TransferDate { get; set; }
    public string BankName { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    public IFormFile ProofFile { get; set; } = null!;
    public string? Notes { get; set; }
}

public class BankTransferResponseDto
{
    public int BankTransferId { get; set; }
    public int PaymentId { get; set; }
    public int TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public int PropertyId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string BankName { get; set; } = string.Empty;
    public DateTime TransferDate { get; set; }
    public string? ReferenceNumber { get; set; }
    public string ProofFilePath { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime SubmittedDate { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedDate { get; set; }
    public string? RejectionReason { get; set; }
}

public class BankTransferApprovalDto
{
    public int BankTransferId { get; set; }
    public bool Approve { get; set; }
    public string? RejectionReason { get; set; }
}
