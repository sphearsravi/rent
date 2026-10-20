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
    public async Task<IActionResult> Get(int tenantId, int invoiceId, [FromQuery] string? culture = null)
    {
        if (!await CallerOwnsTenantAsync(tenantId))
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

        // Completed payments applied to this invoice — rendered as the
        // "Payments received" table; the latest one drives "Last payment".
        var completed = await _context.Payments
            .AsNoTracking()
            .Where(p => p.InvoiceId == invoiceId
                && p.Status == RentCollection.Core.Enums.PaymentStatus.Completed)
            .OrderBy(p => p.PaymentDate)
            .Select(p => new { p.PaymentDate, p.PaymentType, p.TransactionReference, p.ReceiptNumber, p.AmountPaid })
            .ToListAsync();
        DateTime? paidOn = completed.Count > 0 ? completed[^1].PaymentDate : null;

        // Invoices don't have a number column yet; use a stable INV-{id:D6} format
        // (mirrors what the receipts controller does for missing receipt numbers).
        var invoiceNumber = $"INV-{invoice.InvoiceId:D6}";

        // Render culture — explicit ?culture= wins, else the request's UI culture
        // (set by the localization middleware from the cookie / Accept-Language).
        var lang = !string.IsNullOrWhiteSpace(culture)
            ? culture!
            : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var isArabic = lang.StartsWith("ar", StringComparison.OrdinalIgnoreCase);
        var arCulture = CultureInfo.GetCultureInfo("ar-KW");

        // Arabic month name but WESTERN digits for the year (app i18n rule).
        string PeriodLabel(DateTime d) => isArabic
            ? $"{d.ToString("MMMM", arCulture)} {d.Year}"
            : d.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        // Until invoices grow real line items, render a single line that
        // describes the invoice type + period. Keeps the PDF non-empty and
        // self-explanatory. Localized when Arabic.
        var typeLabel = isArabic
            ? (invoice.InvoiceType switch
            {
                "Deposit" => "مبلغ التأمين",
                "Insurance" => "التأمين",
                "LateFee" => "غرامة تأخير",
                "MaintenanceFee" => "الصيانة",
                "Other" => "أخرى",
                _ => "الإيجار",
            })
            : invoice.InvoiceType;
        var lineDescription = $"{typeLabel} — {PeriodLabel(invoice.InvoiceDate)}";

        string MethodLabel(RentCollection.Core.Enums.PaymentType t) => t switch
        {
            RentCollection.Core.Enums.PaymentType.KNET         => "K-NET",
            RentCollection.Core.Enums.PaymentType.BankTransfer => isArabic ? "تحويل بنكي" : "Bank transfer",
            RentCollection.Core.Enums.PaymentType.Cash         => isArabic ? "نقداً" : "Cash",
            RentCollection.Core.Enums.PaymentType.Cheque       => isArabic ? "شيك" : "Cheque",
            _ => t.ToString(),
        };

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
            Culture = lang,
            Notes = invoice.Notes,
            // Drives the PDF title — "DEPOSIT INVOICE" / "INSURANCE INVOICE" / etc.
            InvoiceType = string.IsNullOrEmpty(invoice.InvoiceType) ? "Rent" : invoice.InvoiceType,
            // New (invoice refinements):
            BillingPeriod = PeriodLabel(invoice.InvoiceDate),
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
            // 2026-10 redesign: contact, lease term, payments received.
            TenantPhone = invoice.Tenant?.Phone,
            TenantEmail = invoice.Tenant?.Email,
            LeaseStartDate = invoice.Lease?.LeaseStartDate,
            LeaseEndDate = invoice.Lease?.LeaseEndDate,
            GeneratedAt = DateTime.UtcNow,
            Payments = completed.Select(p => new InvoicePdfPayment(
                p.PaymentDate,
                MethodLabel(p.PaymentType),
                p.TransactionReference ?? p.ReceiptNumber,
                p.AmountPaid)).ToList(),
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
    private async Task<bool> CallerOwnsTenantAsync(int tenantId)
    {
        var claim = User.FindFirstValue("tenantId");
        if (int.TryParse(claim, out var claimTenantId) && claimTenantId == tenantId)
            return true;

        // Cookie admin path. SECURITY (audit C7): the Invoice table has no company
        // filter, so don't wave through "any admin". The Tenant table DOES have a
        // company query filter — this AnyAsync therefore returns true only when the
        // tenant is inside the caller's scoped company (SuperAdmin bypasses and
        // sees all).
        var hasCookieAuth = User.Identities.Any(i =>
            i.AuthenticationType == CookieAuthenticationDefaults.AuthenticationScheme);
        if (!hasCookieAuth) return false;
        return await _context.Tenants.AnyAsync(t => t.TenantId == tenantId);
    }
}
