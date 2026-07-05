using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Application.Interfaces;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.Controllers.Api;

/// <summary>
/// Tenant-facing invoice PDF download. Mirrors <see cref="ReceiptsController"/>:
///   - accepts JWT (Flutter / React tenant clients) or cookie auth (Razor admin / tenant portal)
///   - tenant JWT must match the invoice's tenantId (IDOR guard)
///   - returns application/pdf with a Content-Disposition filename so browsers prompt to save
///
/// Unlike the receipt endpoint, the invoice PDF is available regardless of
/// payment status — even unpaid invoices need a PDF view.
/// </summary>
[ApiController]
[Route("api/tenant")]
public class InvoicesPdfController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IPdfService _pdf;
    private readonly ILogger<InvoicesPdfController> _logger;

    public InvoicesPdfController(
        ApplicationDbContext context,
        IPdfService pdf,
        ILogger<InvoicesPdfController> logger)
    {
        _context = context;
        _pdf = pdf;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/tenant/{tenantId}/invoices/{invoiceId}/invoice.pdf
    /// </summary>
    [HttpGet("{tenantId}/invoices/{invoiceId}/invoice.pdf")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + CookieAuthenticationDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Get(int tenantId, int invoiceId)
    {
        if (!CallerOwnsTenant(tenantId))
        {
            _logger.LogWarning(
                "Invoice PDF access denied. Caller tenant claim does not match {TenantId}.",
                tenantId);
            return Forbid();
        }

        var invoice = await _context.Invoices
            .AsNoTracking()
            .Include(i => i.Tenant)
                .ThenInclude(t => t!.Company)
            .Include(i => i.Lease)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .FirstOrDefaultAsync(i => i.InvoiceId == invoiceId && i.TenantId == tenantId);

        if (invoice == null) return NotFound();

        // Most recent Completed payment date for this invoice — drives the
        // "Paid on …" row in the totals block. Only queried when the invoice
        // actually has a paid amount to display.
        DateTime? paidOn = null;
        if (invoice.PaidAmount > 0)
        {
            paidOn = await _context.Payments
                .Where(p => p.InvoiceId == invoiceId
                    && p.Status == RentCollection.Core.Enums.PaymentStatus.Completed)
                .OrderByDescending(p => p.PaymentDate)
                .Select(p => (DateTime?)p.PaymentDate)
                .FirstOrDefaultAsync();
        }

        // Invoices don't have a number column yet; use a stable INV-{id:D6} format
        // (mirrors what the receipts controller does for missing receipt numbers).
        var invoiceNumber = $"INV-{invoice.InvoiceId:D6}";

        // Until invoices grow real line items, render a single line that
        // describes the invoice type + period. Keeps the PDF non-empty and
        // self-explanatory.
        var lineDescription = string.Format(
            CultureInfo.InvariantCulture,
            "{0} — {1:MMMM yyyy}",
            invoice.InvoiceType,
            invoice.InvoiceDate);

        var model = new InvoicePdfModel
        {
            InvoiceNumber = invoiceNumber,
            InvoiceDate = invoice.InvoiceDate,
            DueDate = invoice.DueDate,
            Status = invoice.Status,
            TenantName = invoice.Tenant != null
                ? $"{invoice.Tenant.FirstName} {invoice.Tenant.LastName}".Trim()
                : "-",
            PropertyName = invoice.Lease?.Unit?.Property?.PropertyName ?? "-",
            UnitNumber = invoice.Lease?.Unit?.UnitNumber ?? "-",
            CompanyName = invoice.Tenant?.Company?.CompanyName,
            TotalAmount = invoice.TotalAmount,
            PaidAmount = invoice.PaidAmount,
            BalanceAmount = invoice.TotalAmount - invoice.PaidAmount,
            Currency = "KWD",
            Notes = invoice.Notes,
            // Drives the PDF title — "DEPOSIT INVOICE" / "INSURANCE INVOICE" / etc.
            InvoiceType = string.IsNullOrEmpty(invoice.InvoiceType) ? "Rent" : invoice.InvoiceType,
            // New (invoice refinements):
            BillingPeriod = invoice.InvoiceDate.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
            TenantPaciNumber = invoice.Tenant?.PACINumber,
            PropertyPaciNumber = invoice.Lease?.Unit?.Property?.PACINumber,
            PaidDate = paidOn,
            // Address composition for the UNIT column — pulled straight from
            // Property. Nulls are fine; the renderer skips empty segments.
            // UnitFloor is null here because the Unit entity has no floor
            // field yet; surface it later when added to the schema.
            UnitFloor = null,
            PropertyBuildingNumber = invoice.Lease?.Unit?.Property?.BuildingNumber,
            PropertyStreet = invoice.Lease?.Unit?.Property?.Street,
            PropertyBlock = invoice.Lease?.Unit?.Property?.Block,
            PropertyArea = invoice.Lease?.Unit?.Property?.Area,
            PropertyGovernorate = invoice.Lease?.Unit?.Property?.Governorate,
            Lines = new()
            {
                new InvoicePdfLine
                {
                    Description = lineDescription,
                    Amount = invoice.TotalAmount,
                },
            },
        };

        var bytes = _pdf.RenderInvoice(model);
        return File(bytes, "application/pdf", $"{invoiceNumber}.pdf");
    }

    /// <summary>
    /// Either the JWT-authenticated tenant whose `tenantId` claim matches the
    /// route, OR any cookie-authenticated admin (the cookie scheme is used by
    /// the MVC admin portal — company-scope filtering happens via the global
    /// query filter, so admins only see their own company's invoices).
    /// </summary>
    private bool CallerOwnsTenant(int tenantId)
    {
        var claim = User.FindFirstValue("tenantId");
        if (int.TryParse(claim, out var claimTenantId) && claimTenantId == tenantId)
            return true;

        return User.Identities.Any(i =>
            i.AuthenticationType == CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
