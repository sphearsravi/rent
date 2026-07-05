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

    // GET: CashPayment/Record
    public async Task<IActionResult> Record()
    {
        await PopulateInvoiceDropdownAsync();
        return View(new CashPaymentDto { PaymentDate = DateTime.UtcNow });
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
                .Include(i => i.Lease).ThenInclude(l => l.Unit)
                .FirstOrDefaultAsync(i => i.InvoiceId == dto.InvoiceId.Value)
            : null;

        if (invoice == null)
        {
            ModelState.AddModelError(nameof(dto.InvoiceId), "Invoice not found.");
        }
        else
        {
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
            await PopulateInvoiceDropdownAsync();
            return View(dto);
        }

        try
        {
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

            TempData["SuccessMessage"] =
                $"Cash payment recorded (receipt {result.ReceiptNumber}). " +
                $"Pending verification by an approver before the invoice is settled.";
            return RedirectToAction("Index", "PaymentApproval");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record cash payment for invoice {InvoiceId}", dto.InvoiceId);
            ModelState.AddModelError(string.Empty, "Could not record the cash payment.");
            await PopulateInvoiceDropdownAsync();
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
        var invoices = await _context.Invoices
            .AsNoTracking()
            .Where(i => i.Status == "Pending" || i.Status == "Overdue"
                     || i.Status == "Partially Paid" || i.Status == "Pending Verification")
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
