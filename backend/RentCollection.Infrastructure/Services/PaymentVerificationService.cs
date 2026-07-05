using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RentCollection.Application.DTOs;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Enums;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Infrastructure.Services;

/// <summary>
/// Service for admin payment verification, mismatch detection, and reconciliation
/// </summary>
public class PaymentVerificationService : IPaymentVerificationService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<PaymentVerificationService> _logger;

    public PaymentVerificationService(
        ApplicationDbContext context,
        ILogger<PaymentVerificationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<AdminPaymentVerificationDto>> GetPaymentsForVerificationAsync(PaymentVerificationFilterDto? filter = null)
    {
        try
        {
            var query = _context.Payments
                .Include(p => p.Tenant)
                .Include(p => p.Lease)
                    .ThenInclude(l => l.Unit)
                        .ThenInclude(u => u.Property)
                .Include(p => p.Invoice)
                .AsQueryable();

            // Apply filters
            if (filter != null)
            {
                if (!string.IsNullOrEmpty(filter.CivilID))
                {
                    query = query.Where(p => p.CivilID == filter.CivilID || p.Tenant.GovernmentId == filter.CivilID);
                }

                if (filter.LeaseId.HasValue)
                {
                    query = query.Where(p => p.LeaseId == filter.LeaseId);
                }

                if (filter.PropertyId.HasValue)
                {
                    query = query.Where(p => p.PropertyId == filter.PropertyId);
                }

                if (!string.IsNullOrEmpty(filter.KNETMID))
                {
                    query = query.Where(p => p.KNETMIDUsed == filter.KNETMID);
                }

                if (filter.HasMismatch.HasValue)
                {
                    query = query.Where(p => p.HasMismatch == filter.HasMismatch);
                }

                if (filter.StartDate.HasValue)
                {
                    query = query.Where(p => p.PaymentDate >= filter.StartDate);
                }

                if (filter.EndDate.HasValue)
                {
                    query = query.Where(p => p.PaymentDate <= filter.EndDate);
                }

                if (!string.IsNullOrEmpty(filter.Status))
                {
                    if (Enum.TryParse<PaymentStatus>(filter.Status, true, out var status))
                    {
                        query = query.Where(p => p.Status == status);
                    }
                }
            }

            var payments = await query
                .OrderByDescending(p => p.PaymentDate)
                .Select(p => new AdminPaymentVerificationDto
                {
                    PaymentId = p.PaymentId,
                    ReceiptNumber = p.ReceiptNumber ?? $"RCPT-{p.PaymentId:D8}",
                    PaymentDate = p.PaymentDate,
                    Amount = p.AmountPaid,
                    Status = p.Status.ToString(),
                    PaymentType = p.PaymentType.ToString(),

                    // Tenant Info
                    TenantId = p.TenantId,
                    TenantName = $"{p.Tenant.FirstName} {p.Tenant.LastName}".Trim(),
                    TenantCivilID = p.Tenant.GovernmentId ?? string.Empty,

                    // Payment Civil ID (may differ from tenant)
                    PaymentCivilID = p.CivilID,

                    // Lease Info
                    LeaseId = p.LeaseId,
                    LeaseReference = $"LEASE-{p.LeaseId:D6}",

                    // Building Info
                    PropertyId = p.PropertyId,
                    BuildingName = p.Lease.Unit.Property.PropertyName,
                    UnitNumber = p.Lease.Unit.UnitNumber,

                    // KNET Info
                    BuildingKNETAccountID = p.Lease.Unit.Property.KNETAccountID,
                    BuildingKNETMID = p.Lease.Unit.Property.KNETMID,
                    PaymentKNETAccountID = p.KNETAccountIDUsed,
                    PaymentKNETMID = p.KNETMIDUsed,
                    CheckoutPaymentId = p.CheckoutPaymentId,
                    TransactionReference = p.TransactionReference,

                    // Mismatch Info
                    HasMismatch = p.HasMismatch,
                    MismatchDetails = p.MismatchDetails,

                    // Computed mismatch flags
                    CivilIDMismatch = !string.IsNullOrEmpty(p.CivilID) && p.CivilID != p.Tenant.GovernmentId,
                    BuildingMismatch = p.PropertyId != p.Lease.Unit.PropertyId,
                    KNETMIDMismatch = !string.IsNullOrEmpty(p.KNETMIDUsed) &&
                                     !string.IsNullOrEmpty(p.Lease.Unit.Property.KNETMID) &&
                                     p.KNETMIDUsed != p.Lease.Unit.Property.KNETMID
                })
                .ToListAsync();

            return payments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payments for verification");
            throw;
        }
    }

    public async Task<List<AdminPaymentVerificationDto>> GetMismatchedPaymentsAsync()
    {
        return await GetPaymentsForVerificationAsync(new PaymentVerificationFilterDto { HasMismatch = true });
    }

    public async Task<bool> ResolveMismatchAsync(int paymentId, string resolvedBy, string? resolutionNotes)
    {
        try
        {
            var payment = await _context.Payments.FindAsync(paymentId);
            if (payment == null)
            {
                _logger.LogWarning("Payment {PaymentId} not found for mismatch resolution", paymentId);
                return false;
            }

            // Update payment
            payment.HasMismatch = false;
            payment.MismatchDetails = $"[RESOLVED by {resolvedBy} at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC] {resolutionNotes}\n{payment.MismatchDetails}";
            payment.ModifiedDate = DateTime.UtcNow;

            // Update audit records
            var auditRecords = await _context.PaymentAudits
                .Where(a => a.PaymentId == paymentId && a.IsMismatch && !a.IsResolved)
                .ToListAsync();

            foreach (var audit in auditRecords)
            {
                audit.IsResolved = true;
                audit.ResolvedBy = resolvedBy;
                audit.ResolvedDate = DateTime.UtcNow;
                audit.ResolutionNotes = resolutionNotes;
            }

            // Create resolution audit record
            var resolutionAudit = new Core.Entities.PaymentAudit
            {
                PaymentId = paymentId,
                AuditType = "MismatchResolved",
                AuditTimestamp = DateTime.UtcNow,
                Amount = payment.AmountPaid,
                IsMismatch = false,
                Notes = $"Mismatch resolved by {resolvedBy}. Notes: {resolutionNotes}",
                IsResolved = true,
                ResolvedBy = resolvedBy,
                ResolvedDate = DateTime.UtcNow
            };
            _context.PaymentAudits.Add(resolutionAudit);

            await _context.SaveChangesAsync();

            _logger.LogInformation("Payment {PaymentId} mismatch resolved by {ResolvedBy}", paymentId, resolvedBy);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving mismatch for Payment {PaymentId}", paymentId);
            return false;
        }
    }

    public async Task<List<PaymentAuditDto>> GetPaymentAuditHistoryAsync(int paymentId)
    {
        return await _context.PaymentAudits
            .Where(a => a.PaymentId == paymentId)
            .OrderByDescending(a => a.AuditTimestamp)
            .Select(a => new PaymentAuditDto
            {
                PaymentAuditId = a.PaymentAuditId,
                PaymentId = a.PaymentId,
                AuditType = a.AuditType,
                AuditTimestamp = a.AuditTimestamp,
                Amount = a.Amount ?? 0,
                IsMismatch = a.IsMismatch,
                MismatchType = a.MismatchType,
                ExpectedValue = a.ExpectedCivilID ?? a.ExpectedKNETMID ?? a.ExpectedBuildingId.ToString(),
                ActualValue = a.ActualCivilID ?? a.ActualKNETMID ?? a.ActualBuildingId.ToString(),
                Details = a.MismatchDetails ?? a.Notes,
                IsResolved = a.IsResolved,
                ResolvedBy = a.ResolvedBy,
                ResolvedAt = a.ResolvedDate
            })
            .ToListAsync();
    }

    public async Task<ReconciliationSummaryDto> GetReconciliationSummaryAsync(DateTime startDate, DateTime endDate, int? propertyId = null)
    {
        try
        {
            var query = _context.Payments
                .Include(p => p.Lease)
                    .ThenInclude(l => l.Unit)
                        .ThenInclude(u => u.Property)
                .Where(p => p.PaymentDate >= startDate && p.PaymentDate <= endDate);

            if (propertyId.HasValue)
            {
                query = query.Where(p => p.PropertyId == propertyId);
            }

            var payments = await query.ToListAsync();

            var summary = new ReconciliationSummaryDto
            {
                StartDate = startDate,
                EndDate = endDate,
                PropertyId = propertyId,
                TotalPayments = payments.Count,
                TotalAmount = payments.Sum(p => p.AmountPaid),
                CompletedPayments = payments.Count(p => p.Status == PaymentStatus.Completed),
                CompletedAmount = payments.Where(p => p.Status == PaymentStatus.Completed).Sum(p => p.AmountPaid),
                PendingPayments = payments.Count(p => p.Status == PaymentStatus.Pending),
                PendingAmount = payments.Where(p => p.Status == PaymentStatus.Pending).Sum(p => p.AmountPaid),
                FailedPayments = payments.Count(p => p.Status == PaymentStatus.Failed),
                FailedAmount = payments.Where(p => p.Status == PaymentStatus.Failed).Sum(p => p.AmountPaid),
                MismatchedPayments = payments.Count(p => p.HasMismatch),
                MismatchedAmount = payments.Where(p => p.HasMismatch).Sum(p => p.AmountPaid)
            };

            // Get audit resolution counts
            var unresolvedAudits = await _context.PaymentAudits
                .Where(a => a.IsMismatch && !a.IsResolved &&
                       payments.Select(p => p.PaymentId).Contains(a.PaymentId))
                .CountAsync();

            var resolvedAudits = await _context.PaymentAudits
                .Where(a => a.IsMismatch && a.IsResolved &&
                       payments.Select(p => p.PaymentId).Contains(a.PaymentId))
                .CountAsync();

            summary.UnresolvedMismatches = unresolvedAudits;
            summary.ResolvedMismatches = resolvedAudits;

            // Group by building (null-safe for payments without lease/unit/property)
            summary.ByBuilding = payments
                .GroupBy(p => new { p.PropertyId, PropertyName = p.Lease?.Unit?.Property?.PropertyName ?? "Unknown", KNETAccountID = p.Lease?.Unit?.Property?.KNETAccountID, KNETMID = p.Lease?.Unit?.Property?.KNETMID })
                .Select(g => new BuildingReconciliationDto
                {
                    PropertyId = g.Key.PropertyId,
                    PropertyName = g.Key.PropertyName,
                    KNETAccountID = g.Key.KNETAccountID,
                    KNETMID = g.Key.KNETMID,
                    PaymentCount = g.Count(),
                    TotalAmount = g.Sum(p => p.AmountPaid),
                    SettledAmount = g.Where(p => p.Status == PaymentStatus.Completed).Sum(p => p.AmountPaid),
                    PendingAmount = g.Where(p => p.Status == PaymentStatus.Pending).Sum(p => p.AmountPaid),
                    MismatchCount = g.Count(p => p.HasMismatch)
                })
                .OrderBy(b => b.PropertyName)
                .ToList();

            // Group by KNET Account
            summary.ByKNETAccount = payments
                .Where(p => !string.IsNullOrEmpty(p.KNETAccountIDUsed))
                .GroupBy(p => new { p.KNETAccountIDUsed, p.KNETMIDUsed })
                .Select(g => new KNETAccountReconciliationDto
                {
                    KNETAccountID = g.Key.KNETAccountIDUsed,
                    KNETMID = g.Key.KNETMIDUsed,
                    PaymentCount = g.Count(),
                    TotalAmount = g.Sum(p => p.AmountPaid),
                    SettledAmount = g.Where(p => p.Status == PaymentStatus.Completed).Sum(p => p.AmountPaid),
                    BuildingNames = g.Select(p => p.Lease?.Unit?.Property?.PropertyName ?? "Unknown").Distinct().ToList()
                })
                .OrderBy(k => k.KNETAccountID)
                .ToList();

            if (propertyId.HasValue)
            {
                var property = await _context.Properties.FindAsync(propertyId);
                summary.PropertyName = property?.PropertyName;
            }

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating reconciliation summary");
            throw;
        }
    }

    public async Task<List<KNETReconciliationDto>> GetKNETReconciliationDataAsync(DateTime startDate, DateTime endDate, int? propertyId = null)
    {
        var query = _context.KNETReconciliations
            .Include(r => r.Property)
            .Where(r => r.TransactionDate >= startDate && r.TransactionDate <= endDate);

        if (propertyId.HasValue)
        {
            query = query.Where(r => r.PropertyId == propertyId);
        }

        return await query
            .OrderByDescending(r => r.TransactionDate)
            .Select(r => new KNETReconciliationDto
            {
                KNETReconciliationId = r.KNETReconciliationId,
                PaymentId = r.PaymentId,
                PropertyId = r.PropertyId,
                PropertyName = r.Property.PropertyName,
                TransactionId = r.TransactionId,
                CheckoutPaymentId = r.CheckoutPaymentId,
                Amount = r.Amount,
                SettledAmount = r.SettledAmount,
                DiscrepancyAmount = r.DiscrepancyAmount,
                Currency = r.Currency,
                TransactionDate = r.TransactionDate,
                SettlementDate = r.SettlementDate,
                ReconciliationStatus = r.ReconciliationStatus,
                KNETAccountID = r.KNETAccountID,
                KNETMID = r.KNETMID,
                BankName = r.BankName,
                BankAccountNumber = r.BankAccountNumber
            })
            .ToListAsync();
    }
}
