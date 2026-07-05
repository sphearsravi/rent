namespace RentCollection.Core.Entities;

/// <summary>
/// Tracks payment audit logs, mismatches, and reconciliation data
/// </summary>
public class PaymentAudit
{
    public int PaymentAuditId { get; set; }
    public int PaymentId { get; set; }

    // Audit Event Details
    public string AuditType { get; set; } = string.Empty;  // Created, Updated, Mismatch, Duplicate, Reconciled
    public DateTime AuditTimestamp { get; set; } = DateTime.UtcNow;
    public string? AuditedBy { get; set; }  // User who triggered the audit (admin username or "System")

    // Mismatch Detection
    public bool IsMismatch { get; set; } = false;
    public string? MismatchType { get; set; }  // CivilIdMismatch, BuildingMismatch, KNETMismatch, DuplicatePayment
    public string? MismatchDetails { get; set; }

    // Expected vs Actual Values (for mismatch tracking)
    public string? ExpectedCivilID { get; set; }
    public string? ActualCivilID { get; set; }
    public int? ExpectedBuildingId { get; set; }
    public int? ActualBuildingId { get; set; }
    public string? ExpectedKNETMID { get; set; }
    public string? ActualKNETMID { get; set; }
    public int? ExpectedLeaseId { get; set; }
    public int? ActualLeaseId { get; set; }

    // Resolution Status
    public bool IsResolved { get; set; } = false;
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedDate { get; set; }
    public string? ResolutionNotes { get; set; }

    // Additional Context
    public string? TransactionReference { get; set; }
    public decimal? Amount { get; set; }
    public string? Notes { get; set; }

    // Navigation
    public Payment Payment { get; set; } = null!;
}

/// <summary>
/// Tracks duplicate payment attempts for idempotency
/// </summary>
public class PaymentIdempotency
{
    public int PaymentIdempotencyId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;  // Unique key (e.g., LeaseId_InvoiceId_Amount_Date)
    public int? PaymentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }  // Keys expire after 24 hours
    public bool IsProcessed { get; set; } = false;
    public string? Status { get; set; }  // Pending, Completed, Failed

    // Cached gateway redirect URL so duplicate initiations within the same session
    // resume the same KNET checkout instead of starting a new one.
    public string? RedirectUrl { get; set; }

    // Request Details
    public int LeaseId { get; set; }
    public int InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string? CivilID { get; set; }
    public int PropertyId { get; set; }

    // Navigation
    public Payment? Payment { get; set; }
}

/// <summary>
/// KNET Reconciliation record for bank settlement matching
/// </summary>
public class KNETReconciliation
{
    public int KNETReconciliationId { get; set; }
    public int PaymentId { get; set; }
    public int PropertyId { get; set; }  // Building

    // KNET Transaction Details
    public string? TransactionId { get; set; }
    public string? CheckoutPaymentId { get; set; }
    public string? KNETAccountID { get; set; }
    public string? KNETMID { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "KWD";
    public DateTime TransactionDate { get; set; }

    // Bank Settlement Details
    public string? BankSettlementRef { get; set; }
    public DateTime? SettlementDate { get; set; }
    public decimal? SettledAmount { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }

    // Reconciliation Status
    public string ReconciliationStatus { get; set; } = "Pending";  // Pending, Matched, Unmatched, Disputed
    public DateTime? ReconciledDate { get; set; }
    public string? ReconciledBy { get; set; }
    public string? Notes { get; set; }

    // Discrepancy Tracking
    public bool HasDiscrepancy { get; set; } = false;
    public decimal? DiscrepancyAmount { get; set; }
    public string? DiscrepancyReason { get; set; }

    // Navigation
    public Payment Payment { get; set; } = null!;
    public Property Property { get; set; } = null!;
}
