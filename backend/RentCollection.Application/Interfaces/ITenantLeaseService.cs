using RentCollection.Application.DTOs;

namespace RentCollection.Application.Interfaces;

/// <summary>
/// Service for handling multi-lease tenant operations
/// </summary>
public interface ITenantLeaseService
{
    /// <summary>
    /// Login tenant using Civil ID and fetch all active leases
    /// </summary>
    Task<TenantLoginResponseDto> LoginByCivilIdAsync(TenantLoginRequestDto request);

    /// <summary>
    /// Get all active leases for a tenant by Civil ID
    /// </summary>
    Task<List<LeaseInfoDto>> GetActiveLeasesByCivilIdAsync(string civilId);

    /// <summary>
    /// Get lease payment info including building KNET configuration
    /// </summary>
    Task<LeasePaymentInfoDto?> GetLeasePaymentInfoAsync(int leaseId, string civilId);

    /// <summary>
    /// Get pending invoices for a specific lease
    /// </summary>
    Task<List<InvoiceInfoDto>> GetPendingInvoicesForLeaseAsync(int leaseId);

    /// <summary>
    /// Validate that Civil ID matches the lease owner
    /// </summary>
    Task<(bool IsValid, string? ErrorMessage)> ValidateCivilIdForLeaseAsync(string civilId, int leaseId);

    /// <summary>
    /// Get payment receipt details
    /// </summary>
    Task<PaymentReceiptDto?> GetPaymentReceiptAsync(int paymentId);
}
