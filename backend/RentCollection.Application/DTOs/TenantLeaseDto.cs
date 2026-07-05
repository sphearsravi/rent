namespace RentCollection.Application.DTOs;

/// <summary>
/// DTO for tenant login with Civil ID
/// </summary>
public class TenantLoginRequestDto
{
    public string CivilID { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; } = false;
}

/// <summary>
/// DTO for tenant login response with all active leases
/// </summary>
public class TenantLoginResponseDto
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Token { get; set; }
    public TenantInfoDto? Tenant { get; set; }
    public List<LeaseInfoDto> ActiveLeases { get; set; } = new();
}

/// <summary>
/// Basic tenant information
/// </summary>
public class TenantInfoDto
{
    public int TenantId { get; set; }
    public string CivilID { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

/// <summary>
/// Lease information with building details
/// </summary>
public class LeaseInfoDto
{
    public int LeaseId { get; set; }
    public int UnitId { get; set; }
    public string UnitNumber { get; set; } = string.Empty;
    public int PropertyId { get; set; }  // BuildingID
    public string BuildingName { get; set; } = string.Empty;
    public string BuildingAddress { get; set; } = string.Empty;
    public string? BuildingPACINumber { get; set; }
    public DateTime LeaseStartDate { get; set; }
    public DateTime LeaseEndDate { get; set; }
    public decimal MonthlyRent { get; set; }
    public string Status { get; set; } = string.Empty;

    // Building KNET Configuration
    public bool AcceptKNET { get; set; }
    public string? KNETAccountID { get; set; }
    public string? KNETMID { get; set; }

    // Outstanding balance
    public decimal TotalOutstanding { get; set; }
    public int PendingInvoicesCount { get; set; }
}

/// <summary>
/// DTO for lease selection in payment
/// </summary>
public class LeasePaymentInfoDto
{
    public int LeaseId { get; set; }
    public int TenantId { get; set; }
    public string CivilID { get; set; } = string.Empty;
    public int PropertyId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public string UnitNumber { get; set; } = string.Empty;
    public decimal MonthlyRent { get; set; }

    // KNET Configuration for this building
    public string? KNETAccountID { get; set; }
    public string? KNETMID { get; set; }
    public string? KnetTranportalId { get; set; }
    public string? KnetTranportalPwd { get; set; }
    public string? KnetTerminalResourceKey { get; set; }

    // Pending invoices for this lease
    public List<InvoiceInfoDto> PendingInvoices { get; set; } = new();
}

/// <summary>
/// Invoice information for payment
/// </summary>
public class InvoiceInfoDto
{
    public int InvoiceId { get; set; }
    public int LeaseId { get; set; }
    public DateTime InvoiceDate { get; set; }
    public DateTime DueDate { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal OutstandingAmount => TotalAmount - PaidAmount;
    public string Status { get; set; } = string.Empty;
    public string? InvoiceType { get; set; }
    public bool IsOverdue => DueDate < DateTime.UtcNow && Status != "Paid";
}

/// <summary>
/// Payment receipt/invoice DTO for generation after payment
/// </summary>
public class PaymentReceiptDto
{
    public int PaymentId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }

    // Tenant Info
    public string CivilID { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;

    // Lease Info
    public int LeaseId { get; set; }
    public string LeaseReference { get; set; } = string.Empty;

    // Building Info
    public int PropertyId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public string UnitNumber { get; set; } = string.Empty;

    // Payment Details
    public decimal RentAmount { get; set; }
    public decimal LateFeeAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;

    // KNET Details
    public string? TransactionId { get; set; }
    public string? CheckoutPaymentId { get; set; }
    public string? KNETAccountID { get; set; }
    public string? KNETMID { get; set; }

    // Status
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Admin payment verification DTO with mismatch flags
/// </summary>
public class AdminPaymentVerificationDto
{
    public int PaymentId { get; set; }
    public DateTime PaymentDate { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;

    // Tenant Info
    public int TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string TenantCivilID { get; set; } = string.Empty;  // Civil ID from tenant record

    // Payment Civil ID (may differ from tenant)
    public string? PaymentCivilID { get; set; }  // Civil ID used during payment

    // Lease Info
    public int LeaseId { get; set; }
    public string LeaseReference { get; set; } = string.Empty;

    // Building Info
    public int PropertyId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public string UnitNumber { get; set; } = string.Empty;

    // Payment Details
    public decimal Amount { get; set; }
    public string PaymentType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    // Building's configured KNET credentials
    public string? BuildingKNETAccountID { get; set; }
    public string? BuildingKNETMID { get; set; }

    // KNET credentials actually used for payment
    public string? PaymentKNETAccountID { get; set; }
    public string? PaymentKNETMID { get; set; }

    // Transaction References
    public string? CheckoutPaymentId { get; set; }
    public string? TransactionReference { get; set; }

    // Mismatch Flags
    public bool HasMismatch { get; set; }
    public bool CivilIDMismatch { get; set; }
    public bool BuildingMismatch { get; set; }
    public bool KNETMIDMismatch { get; set; }
    public string? MismatchDetails { get; set; }
}
