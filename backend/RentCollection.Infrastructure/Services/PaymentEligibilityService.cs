using Microsoft.EntityFrameworkCore;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Enums;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Infrastructure.Services;

/// <summary>
/// Sprint 24 — "oldest unpaid first" enforcement. See
/// <see cref="IPaymentEligibilityService"/> for the rule and contract.
///
/// Implementation note: chronological ordering uses InvoiceDate first
/// then InvoiceId as a deterministic tie-breaker for invoices issued
/// on the same day (e.g. rent + insurance billed by the same nightly
/// job). DueDate is NOT the ordering key — issue date is, because
/// a late-issued invoice for an earlier period should still queue first.
/// </summary>
public sealed class PaymentEligibilityService : IPaymentEligibilityService
{
    private readonly ApplicationDbContext _db;

    public PaymentEligibilityService(ApplicationDbContext db) => _db = db;

    public async Task<EligibilityResult> CheckAsync(int invoiceId, CancellationToken ct = default)
    {
        var requested = await _db.Invoices
            .AsNoTracking()
            .Where(i => i.InvoiceId == invoiceId)
            .Select(i => new { i.InvoiceId, i.TenantId })
            .FirstOrDefaultAsync(ct);

        if (requested is null)
        {
            return EligibilityResult.InvoiceNotFound();
        }

        var oldest = await _db.Invoices
            .AsNoTracking()
            .Where(i => i.TenantId == requested.TenantId
                     && i.Status != InvoiceStatuses.Paid
                     && i.Status != InvoiceStatuses.Cancelled)
            .OrderBy(i => i.InvoiceDate)
            .ThenBy(i => i.InvoiceId)
            .Select(i => new
            {
                i.InvoiceId,
                Balance = i.TotalAmount - i.PaidAmount,
                i.InvoiceDate,
                i.InvoiceType,
            })
            .FirstOrDefaultAsync(ct);

        // No outstanding invoices — anything that's still "pending" was
        // already paid or cancelled. Treat as eligible (the controller
        // will surface its own "nothing to pay" message if relevant).
        if (oldest is null) return EligibilityResult.Eligible();

        if (oldest.InvoiceId == requested.InvoiceId)
        {
            return EligibilityResult.Eligible();
        }

        var label = $"{oldest.InvoiceType} for {oldest.InvoiceDate:MMMM yyyy}";
        return EligibilityResult.Blocked(oldest.InvoiceId, oldest.Balance, label);
    }
}
