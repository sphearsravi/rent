using RentCollection.Application.DTOs;

namespace RentCollection.Application.Interfaces;

/// <summary>
/// Service for admin payment verification, mismatch detection, and reconciliation
/// </summary>
public interface IPaymentVerificationService
{
    /// <summary>
    /// Gets all payments with mismatch indicators for admin verification
    /// </summary>
    Task<List<AdminPaymentVerificationDto>> GetPaymentsForVerificationAsync(PaymentVerificationFilterDto? filter = null);

    /// <summary>
    /// Gets payments with mismatch flags
    /// </summary>
    Task<List<AdminPaymentVerificationDto>> GetMismatchedPaymentsAsync();

    /// <summary>
    /// Resolves a payment mismatch (admin verification)
    /// </summary>
    Task<bool> ResolveMismatchAsync(int paymentId, string resolvedBy, string? resolutionNotes);

    /// <summary>
    /// Gets payment audit history
    /// </summary>
    Task<List<PaymentAuditDto>> GetPaymentAuditHistoryAsync(int paymentId);

    /// <summary>
    /// Gets reconciliation summary for a date range
    /// </summary>
    Task<ReconciliationSummaryDto> GetReconciliationSummaryAsync(DateTime startDate, DateTime endDate, int? propertyId = null);

    /// <summary>
    /// Gets KNET settlement data for reconciliation
    /// </summary>
    Task<List<KNETReconciliationDto>> GetKNETReconciliationDataAsync(DateTime startDate, DateTime endDate, int? propertyId = null);
}

/// <summary>
/// Filter options for payment verification
/// </summary>
public class PaymentVerificationFilterDto
{
    public string? CivilID { get; set; }
    public int? LeaseId { get; set; }
    public int? PropertyId { get; set; }
    public string? KNETMID { get; set; }
    public bool? HasMismatch { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Status { get; set; }
}

/// <summary>
/// Payment audit record DTO
/// </summary>
public class PaymentAuditDto
{
    public int PaymentAuditId { get; set; }
    public int PaymentId { get; set; }
    public string AuditType { get; set; } = string.Empty;
    public DateTime AuditTimestamp { get; set; }
    public decimal Amount { get; set; }
    public bool IsMismatch { get; set; }
    public string? MismatchType { get; set; }
    public string? ExpectedValue { get; set; }
    public string? ActualValue { get; set; }
    public string? Details { get; set; }
    public bool IsResolved { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

/// <summary>
/// Reconciliation summary DTO
/// </summary>
public class ReconciliationSummaryDto
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int? PropertyId { get; set; }
    public string? PropertyName { get; set; }
    public int TotalPayments { get; set; }
    public decimal TotalAmount { get; set; }
    public int CompletedPayments { get; set; }
    public decimal CompletedAmount { get; set; }
    public int PendingPayments { get; set; }
    public decimal PendingAmount { get; set; }
    public int FailedPayments { get; set; }
    public decimal FailedAmount { get; set; }
    public int MismatchedPayments { get; set; }
    public decimal MismatchedAmount { get; set; }
    public int ResolvedMismatches { get; set; }
    public int UnresolvedMismatches { get; set; }
    public List<BuildingReconciliationDto> ByBuilding { get; set; } = new();
    public List<KNETAccountReconciliationDto> ByKNETAccount { get; set; } = new();
}

/// <summary>
/// Building-level reconciliation
/// </summary>
public class BuildingReconciliationDto
{
    public int PropertyId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public string? KNETAccountID { get; set; }
    public string? KNETMID { get; set; }
    public int PaymentCount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal SettledAmount { get; set; }
    public decimal PendingAmount { get; set; }
    public int MismatchCount { get; set; }
}

/// <summary>
/// KNET Account-level reconciliation
/// </summary>
public class KNETAccountReconciliationDto
{
    public string? KNETAccountID { get; set; }
    public string? KNETMID { get; set; }
    public int PaymentCount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal SettledAmount { get; set; }
    public List<string> BuildingNames { get; set; } = new();
}

/// <summary>
/// KNET Reconciliation record DTO
/// </summary>
public class KNETReconciliationDto
{
    public int KNETReconciliationId { get; set; }
    public int PaymentId { get; set; }
    public int PropertyId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public string? TransactionId { get; set; }
    public string? CheckoutPaymentId { get; set; }
    public decimal Amount { get; set; }
    public decimal? SettledAmount { get; set; }
    public decimal? DiscrepancyAmount { get; set; }
    public string Currency { get; set; } = "KWD";
    public DateTime TransactionDate { get; set; }
    public DateTime? SettlementDate { get; set; }
    public string ReconciliationStatus { get; set; } = string.Empty;
    public string? KNETAccountID { get; set; }
    public string? KNETMID { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }
}
