using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Core.Enums;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.Controllers.Api;

/// <summary>
/// Tenant-facing receipt downloads. A receipt is only available for a payment
/// whose Status is Completed; otherwise 404. The endpoint accepts either:
///   - a JWT bearer token (Flutter app) — the `tenantId` claim must match the
///     payment's TenantId, or
///   - an authenticated cookie session that owns the payment.
/// </summary>
[ApiController]
[Route("api/payments")]
public class ReceiptsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IPdfService _pdf;
    private readonly ILogger<ReceiptsController> _logger;

    public ReceiptsController(ApplicationDbContext context, IPdfService pdf, ILogger<ReceiptsController> logger)
    {
        _context = context;
        _pdf = pdf;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/payments/{paymentId}/receipt.pdf
    /// </summary>
    [HttpGet("{paymentId}/receipt.pdf")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Get(int paymentId, [FromQuery] string? culture = null)
    {
        var payment = await _context.Payments
            .AsNoTracking()
            .Include(p => p.Tenant)
                .ThenInclude(t => t!.Company)
            .Include(p => p.Lease)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .Include(p => p.Invoice)
            .Include(p => p.CashPayment)
            .FirstOrDefaultAsync(p => p.PaymentId == paymentId);

        if (payment == null) return NotFound();

        // Ownership FIRST — a non-owner must not learn anything about the
        // payment's existence or status. (Red-team #11: was a status-before-
        // ownership oracle across tenants.)
        if (!CallerOwnsPayment(payment))
        {
            _logger.LogWarning("Receipt access denied. Payment {PaymentId} caller mismatch.", paymentId);
            return Forbid();
        }

        // Only completed payments have a receipt.
        if (payment.Status != PaymentStatus.Completed)
            return NotFound(new { message = "Receipt is only available for completed payments." });

        var model = new ReceiptPdfModel
        {
            ReceiptNumber = payment.ReceiptNumber ?? $"RCPT-{payment.PaymentId:D8}",
            PaymentDate = payment.PaymentDate,
            PaymentMethod = payment.PaymentType.ToString(),
            TenantName = payment.Tenant != null
                ? $"{payment.Tenant.FirstName} {payment.Tenant.LastName}".Trim()
                : "-",
            PropertyName = payment.Lease?.Unit?.Property?.PropertyName ?? "-",
            UnitNumber = payment.Lease?.Unit?.UnitNumber ?? "-",
            CompanyName = payment.Tenant?.Company?.CompanyName,
            AmountPaid = payment.AmountPaid,
            Currency = "KWD",
            TransactionReference = payment.TransactionReference,
            InvoiceNumber = payment.InvoiceId.HasValue ? $"INV-{payment.InvoiceId:D6}" : null,
            ManualReceiptNumber = payment.CashPayment?.ManualReceiptNumber,
            Notes = payment.Notes,
            // 2026-10 redesign: what is still owed on the invoice, and the
            // render language (explicit ?culture= wins, else the request culture).
            InvoiceBalanceAfter = payment.Invoice != null
                ? payment.Invoice.TotalAmount - payment.Invoice.PaidAmount
                : null,
            Culture = !string.IsNullOrWhiteSpace(culture)
                ? culture!
                : System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            GeneratedAt = DateTime.UtcNow,
        };

        var bytes = _pdf.RenderReceipt(model);
        return File(bytes, "application/pdf", $"receipt-{model.ReceiptNumber}.pdf");
    }

    /// <summary>
    /// Caller is either:
    ///   - the JWT-authenticated tenant whose claim matches payment.TenantId, OR
    ///   - any cookie-authenticated admin (the cookie scheme is used by the
    ///     MVC admin portal).
    /// </summary>
    private bool CallerOwnsPayment(Payment payment)
    {
        // JWT path: tenant claim must match.
        var claim = User.FindFirstValue("tenantId");
        if (int.TryParse(claim, out var claimTenantId) && claimTenantId == payment.TenantId)
            return true;

        // Cookie path: admin portal. SECURITY (audit C7): Payment has NO company
        // query filter, so "any authenticated admin" would leak other companies'
        // receipts. Verify the payment's company (via its Tenant) against the
        // caller's company claim. SuperAdmin is cross-company by design.
        var hasCookieAuth = User.Identities.Any(i =>
            i.AuthenticationType == Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        if (!hasCookieAuth) return false;
        if (User.IsInRole(RentCollection.Core.Entities.Roles.SuperAdmin)) return true;
        return int.TryParse(User.FindFirstValue("companyId"), out var cid)
            && payment.Tenant?.CompanyId == cid;
    }
}
