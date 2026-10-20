using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RentCollection.Application.DTOs;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.Controllers;

/// <summary>
/// Records in-person cash payments. Cash is collected at the office by a
/// staff member, recorded here, then verified by a different person in
/// PaymentApproval (two-person control). The recorded payment lands in
/// PendingVerification — it does NOT settle the invoice until approved.
/// </summary>
[Authorize(Roles = Roles.Financial)]
public class CashPaymentController : Controller
{
    private readonly ICashPaymentService _cashPaymentService;
    private readonly IPaymentEligibilityService _eligibility;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CashPaymentController> _logger;

    public CashPaymentController(
        ICashPaymentService cashPaymentService,
        IPaymentEligibilityService eligibility,
        ApplicationDbContext context,
        ILogger<CashPaymentController> logger)
    {
        _cashPaymentService = cashPaymentService;
        _eligibility = eligibility;
        _context = context;
        _logger = logger;
    }

    // Company scope: Invoice has no company query filter, so an InvoiceId
    // posted from a tampered form could attach cash to another company's
    // invoice. Guard via the owning Tenant; SuperAdmin is cross-company. A
    // scoped admin with no companyId claim resolves to -1 (deny).
    private int? ScopedCompanyId() =>
        User.IsInRole(Roles.SuperAdmin) ? null
        : int.TryParse(User.FindFirst("companyId")?.Value, out var cid) ? cid : -1;

    private bool CanAccessCompany(int? entityCompanyId)
    {
        if (User.IsInRole(Roles.SuperAdmin)) return true;
        return int.TryParse(User.FindFirst("companyId")?.Value, out var cid)
            && entityCompanyId == cid;
    }

    // GET: CashPayment/Record?tenantId=&invoiceId=
    // Both are optional preselections (Tenants kebab passes tenantId,
    // an invoice page can pass invoiceId).
    public async Task<IActionResult> Record(int? tenantId = null, int? invoiceId = null)
    {
        await PopulateCashFormAsync(tenantId, invoiceId);
        return View(new CashPaymentDto { PaymentDate = KuwaitToday(), InvoiceId = invoiceId });
    }

    /// <summary>
    /// The office works on Kuwait's calendar; the host's DateTime.Today is a
    /// US-zone date and lagged a day every morning.
    /// </summary>
    private static DateTime KuwaitToday()
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;
        }
        catch (TimeZoneNotFoundException)
        {
            return DateTime.UtcNow.AddHours(3).Date;
        }
    }

    /// <summary>
    /// Data for the redesigned Record form: every tenant with open invoices,
    /// each invoice with its balance and whether it is the one the
    /// pay-oldest-first rule allows next. Serialised for the client so the
    /// clerk picks tenant → invoice → amount with the numbers in front of them.
    /// </summary>
    private async Task PopulateCashFormAsync(int? tenantId, int? invoiceId)
    {
        var scope = ScopedCompanyId();
        var rows = await _context.Invoices
            .AsNoTracking()
            .Where(i => i.Status == "Pending" || i.Status == "Overdue"
                     || i.Status == "Partially Paid" || i.Status == "Pending Verification")
            .Where(i => scope == null || (i.Tenant != null && i.Tenant.CompanyId == scope))
            .Select(i => new
            {
                i.InvoiceId,
                i.TenantId,
                TenantName = i.Tenant != null ? (i.Tenant.FirstName + " " + i.Tenant.LastName) : null,
                CivilId = i.Tenant != null ? i.Tenant.PACINumber : null,
                Phone = i.Tenant != null ? i.Tenant.Phone : null,
                Property = (i.Lease != null && i.Lease.Unit != null && i.Lease.Unit.Property != null) ? i.Lease.Unit.Property.PropertyName : null,
                Unit = (i.Lease != null && i.Lease.Unit != null) ? i.Lease.Unit.UnitNumber : null,
                i.InvoiceType,
                i.InvoiceDate,
                i.DueDate,
                i.Status,
                Total = (decimal?)i.TotalAmount,
                Paid = (decimal?)i.PaidAmount,
            })
            .ToListAsync();

        var today = DateTime.Today;
        var tenants = rows
            .GroupBy(r => r.TenantId)
            .Select(g =>
            {
                var first = g.First();
                var invoices = g.OrderBy(r => r.DueDate).ThenBy(r => r.InvoiceId).Select((r, idx) => new
                {
                    id = r.InvoiceId,
                    number = $"INV-{r.InvoiceId:D6}",
                    type = string.IsNullOrEmpty(r.InvoiceType) ? "Rent" : r.InvoiceType,
                    period = r.InvoiceDate.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture),
                    due = r.DueDate.ToString("yyyy-MM-dd"),
                    daysLate = (today - r.DueDate.Date).Days,
                    status = r.Status,
                    total = r.Total ?? 0m,
                    paid = r.Paid ?? 0m,
                    balance = (r.Total ?? 0m) - (r.Paid ?? 0m),
                    property = r.Property,
                    unit = r.Unit,
                    // Pay-oldest-first: only the oldest open invoice is payable now.
                    eligible = idx == 0,
                }).ToList();
                return new
                {
                    id = g.Key,
                    name = (first.TenantName ?? $"Tenant #{g.Key}").Trim(),
                    civilId = first.CivilId,
                    phone = first.Phone,
                    outstanding = invoices.Sum(i => i.balance),
                    invoices,
                };
            })
            .OrderBy(t => t.name)
            .ToList();

        ViewBag.CashFormJson = System.Text.Json.JsonSerializer.Serialize(tenants);
        ViewBag.Today = KuwaitToday();
        ViewBag.PreselectTenantId = tenantId ?? rows.FirstOrDefault(r => r.InvoiceId == invoiceId)?.TenantId;
        ViewBag.PreselectInvoiceId = invoiceId;
    }

    // POST: CashPayment/Record
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Record(CashPaymentDto dto)
    {
        // The invoice drives tenant/property — re-derive them server-side so a
        // tampered form can't attach cash to the wrong tenant.
        if (dto.InvoiceId is null or 0)
        {
            ModelState.AddModelError(nameof(dto.InvoiceId), "Select an invoice.");
        }

        var invoice = dto.InvoiceId.HasValue
            ? await _context.Invoices
                .Include(i => i.Tenant)
                .Include(i => i.Lease).ThenInclude(l => l.Unit)
                .FirstOrDefaultAsync(i => i.InvoiceId == dto.InvoiceId.Value)
            : null;

        if (invoice == null)
        {
            ModelState.AddModelError(nameof(dto.InvoiceId), "Invoice not found.");
        }
        else
        {
            if (!CanAccessCompany(invoice.Tenant?.CompanyId)) return NotFound();

            if (string.Equals(invoice.Status, "Paid", StringComparison.OrdinalIgnoreCase))
                ModelState.AddModelError(nameof(dto.InvoiceId), "Invoice is already paid.");

            var due = decimal.Round(invoice.TotalAmount - invoice.PaidAmount, 3);
            var amt = decimal.Round(dto.Amount, 3);
            if (amt <= 0 || amt > due)
                ModelState.AddModelError(nameof(dto.Amount),
                    $"Amount must be between 0 and the balance ({due:0.000} KWD).");

            // Sprint 24 — oldest-unpaid-first rule applies to staff cash
            // recording too (no override). The rule guards against operator
            // mistakes (clerk picks the wrong row from the dropdown) and
            // keeps the tenant's queue strictly chronological.
            var eligibility = await _eligibility.CheckAsync(invoice.InvoiceId);
            if (!eligibility.IsEligible)
            {
                ModelState.AddModelError(nameof(dto.InvoiceId),
                    $"Cannot collect cash for this invoice yet. {eligibility.Reason}");
            }

            // Server-authoritative tenant/property from the invoice's lease.
            dto.TenantId = invoice.TenantId;
            dto.PropertyId = invoice.Lease?.Unit?.PropertyId ?? 0;
        }

        if (!ModelState.IsValid)
        {
            await PopulateCashFormAsync(dto.TenantId, dto.InvoiceId);
            return View(dto);
        }

        try
        {
            // One transaction around the service (which saves the Payment +
            // CashPayment + invoice flip internally via the SAME scoped
            // DbContext) and the audit row, so a failed audit write can't
            // leave an un-audited cash payment behind. Disposal without
            // Commit rolls everything back.
            await using var tx = await _context.Database.BeginTransactionAsync();

            var result = await _cashPaymentService.RecordCashPaymentAsync(
                dto, User.Identity?.Name ?? "system");

            // Audit the collection event (parity with KNET/approval audits).
            _context.PaymentAudits.Add(new PaymentAudit
            {
                PaymentId = result.PaymentId,
                AuditType = "CashRecorded",
                AuditTimestamp = DateTime.UtcNow,
                Amount = result.Amount,
                AuditedBy = User.Identity?.Name ?? "system",
                IsMismatch = false,
                IsResolved = false,
                Notes = $"Cash collected, receipt {result.ReceiptNumber}, pending verification"
            });
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            TempData["SuccessMessage"] =
                $"Cash payment recorded (receipt {result.ReceiptNumber}). " +
                $"Pending verification by an approver before the invoice is settled.";
            return RedirectToAction("Index", "PaymentApproval");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record cash payment for invoice {InvoiceId}", dto.InvoiceId);
            ModelState.AddModelError(string.Empty, "Could not record the cash payment.");
            await PopulateCashFormAsync(dto.TenantId, dto.InvoiceId);
            return View(dto);
        }
    }

    private async Task PopulateInvoiceDropdownAsync()
    {
        // Only outstanding invoices are payable by cash.
        // Project to a flat, NULLABLE shape instead of materializing the full
        // Invoice→Tenant→Lease→Unit→Property graph: the demo has imported rows
        // with NULLs in columns the entities declare non-nullable, which made EF
        // throw SqlNullValueException while materializing and 500'd the page.
        // A projection reads only these columns as nullable and can't throw, and
        // the LEFT-join style null checks keep orphaned invoices instead of
        // dropping them.
        var scope = ScopedCompanyId();
        var invoices = await _context.Invoices
            .AsNoTracking()
            .Where(i => i.Status == "Pending" || i.Status == "Overdue"
                     || i.Status == "Partially Paid" || i.Status == "Pending Verification")
            // Company scope — only the caller's company's invoices are offered.
            .Where(i => scope == null || (i.Tenant != null && i.Tenant.CompanyId == scope))
            .OrderBy(i => i.DueDate)
            .Select(i => new
            {
                i.InvoiceId,
                First = i.Tenant != null ? i.Tenant.FirstName : null,
                Last = i.Tenant != null ? i.Tenant.LastName : null,
                Property = (i.Lease != null && i.Lease.Unit != null && i.Lease.Unit.Property != null)
                    ? i.Lease.Unit.Property.PropertyName : null,
                Total = (decimal?)i.TotalAmount,
                Paid = (decimal?)i.PaidAmount
            })
            .ToListAsync();

        ViewBag.Invoices = new SelectList(
            invoices.Select(i => new
            {
                i.InvoiceId,
                Label = $"INV-{i.InvoiceId:D6} • " +
                        $"{i.First} {i.Last} • {i.Property} " +
                        $"• Bal {((i.Total ?? 0m) - (i.Paid ?? 0m)):0.000} KWD"
            }),
            "InvoiceId", "Label");
    }
}
