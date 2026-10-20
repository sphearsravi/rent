using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Core.Entities;
using RentCollection.Core.Enums;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.Controllers;

/// <summary>
/// Item 8 — admin-side refund management. Records refunds against existing
/// payments; updating the linked invoice's PaidAmount happens when the refund
/// is marked Settled.
/// </summary>
[Authorize(Roles = Roles.Financial)]
public class RefundsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<RefundsController> _logger;

    public RefundsController(ApplicationDbContext context, ILogger<RefundsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    // Company scope: Refund/Payment have no company query filter. Guard via the
    // payment's owning Tenant; SuperAdmin is cross-company. A scoped admin with
    // no companyId claim resolves to -1 (deny).
    private int? ScopedCompanyId() =>
        User.IsInRole(Roles.SuperAdmin) ? null
        : int.TryParse(User.FindFirst("companyId")?.Value, out var cid) ? cid : -1;

    private bool CanAccessCompany(int? entityCompanyId)
    {
        if (User.IsInRole(Roles.SuperAdmin)) return true;
        return int.TryParse(User.FindFirst("companyId")?.Value, out var cid)
            && entityCompanyId == cid;
    }

    public async Task<IActionResult> Index()
    {
        var scope = ScopedCompanyId();
        var refunds = await _context.Refunds
            .Include(r => r.Payment)
                .ThenInclude(p => p.Tenant)
            .Where(r => scope == null || r.Payment.Tenant.CompanyId == scope)
            .OrderByDescending(r => r.CreatedDate)
            .ToListAsync();
        return View(refunds);
    }

    public async Task<IActionResult> Create(int paymentId)
    {
        var payment = await _context.Payments
            .Include(p => p.Tenant)
            .FirstOrDefaultAsync(p => p.PaymentId == paymentId);
        if (payment == null || !CanAccessCompany(payment.Tenant?.CompanyId)) return NotFound();
        ViewBag.Payment = payment;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int paymentId, decimal amount, string method, string reason, string? referenceNumber)
    {
        var payment = await _context.Payments
            .Include(p => p.Tenant)
            .FirstOrDefaultAsync(p => p.PaymentId == paymentId);
        if (payment == null || !CanAccessCompany(payment.Tenant?.CompanyId)) return NotFound();

        // Only money that actually arrived can be refunded. A Pending /
        // PendingVerification / Rejected / Failed payment has nothing to give
        // back — rejecting it is the right reversal for those.
        if (payment.Status != PaymentStatus.Completed)
        {
            TempData["ErrorMessage"] =
                $"Only Completed payments can be refunded. Payment #{paymentId} is {payment.Status}.";
            return RedirectToAction(nameof(Index));
        }
        if (string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(reason))
        {
            TempData["ErrorMessage"] = "Refund method and reason are required.";
            return RedirectToAction(nameof(Create), new { paymentId });
        }
        if (amount <= 0 || amount > payment.AmountPaid)
        {
            TempData["ErrorMessage"] =
                $"Refund amount must be between 0 and the amount paid ({payment.AmountPaid:0.000}).";
            return RedirectToAction(nameof(Create), new { paymentId });
        }

        // SECURITY (audit M1): cap CUMULATIVE refunds at the amount actually paid.
        // Each refund was individually bounded, but nothing stopped several partial
        // refunds summing beyond payment.AmountPaid and over-crediting.
        var alreadyRefunded = await _context.Refunds
            .Where(r => r.PaymentId == paymentId && r.Status != RefundStatus.Rejected)
            .SumAsync(r => (decimal?)r.Amount) ?? 0m;
        if (amount <= 0 || alreadyRefunded + amount > payment.AmountPaid)
        {
            TempData["ErrorMessage"] =
                $"Invalid amount. Paid {payment.AmountPaid:0.000}, already refunded {alreadyRefunded:0.000}, "
                + $"remaining refundable {(payment.AmountPaid - alreadyRefunded):0.000}.";
            return RedirectToAction(nameof(Create), new { paymentId });
        }

        _context.Refunds.Add(new Refund
        {
            PaymentId = paymentId,
            Amount = amount,
            Method = method.Trim(),
            Reason = reason.Trim(),
            ReferenceNumber = referenceNumber,
            Status = RefundStatus.Pending,
            CreatedBy = User.Identity?.Name,
        });
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Refund queued for payment #{paymentId}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.AnyAdmin)]
    public async Task<IActionResult> Approve(int id)
    {
        var refund = await _context.Refunds
            .Include(r => r.Payment)
                .ThenInclude(p => p.Tenant)
            .FirstOrDefaultAsync(r => r.RefundId == id);
        if (refund == null || !CanAccessCompany(refund.Payment?.Tenant?.CompanyId)) return NotFound();
        if (refund.Status != RefundStatus.Pending)
        {
            TempData["ErrorMessage"] = "Only Pending refunds can be approved.";
            return RedirectToAction(nameof(Index));
        }
        // SECURITY (audit M1): separation of duties — the creator cannot approve
        // their own refund.
        if (string.Equals(refund.CreatedBy, User.Identity?.Name, StringComparison.OrdinalIgnoreCase))
        {
            TempData["ErrorMessage"] = "You cannot approve a refund you created — a different admin must approve it.";
            return RedirectToAction(nameof(Index));
        }
        refund.Status = RefundStatus.Approved;
        refund.ApprovedBy = User.Identity?.Name;
        refund.ApprovedDate = DateTime.UtcNow;
        try
        {
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Refund #{id} approved.";
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["ErrorMessage"] = "Refund was changed by someone else — refresh and retry.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.AnyAdmin)]
    public async Task<IActionResult> Settle(int id)
    {
        var refund = await _context.Refunds
            .Include(r => r.Payment)
                .ThenInclude(p => p.Tenant)
            .FirstOrDefaultAsync(r => r.RefundId == id);
        if (refund == null || !CanAccessCompany(refund.Payment?.Tenant?.CompanyId)) return NotFound();
        if (refund.Status != RefundStatus.Approved)
        {
            TempData["ErrorMessage"] = "Only Approved refunds can be settled.";
            return RedirectToAction(nameof(Index));
        }
        // SECURITY (audit M1): the creator cannot settle (move the money for) their
        // own refund. Combined with the approve check, at least two distinct admins
        // are involved in every refund that reverses funds.
        if (string.Equals(refund.CreatedBy, User.Identity?.Name, StringComparison.OrdinalIgnoreCase))
        {
            TempData["ErrorMessage"] = "You cannot settle a refund you created — a different admin must settle it.";
            return RedirectToAction(nameof(Index));
        }

        refund.Status = RefundStatus.Settled;
        refund.SettledDate = DateTime.UtcNow;

        // Reverse the impact on the invoice — only on settle, not on approve.
        if (refund.Payment.InvoiceId.HasValue)
        {
            var invoice = await _context.Invoices.FindAsync(refund.Payment.InvoiceId.Value);
            if (invoice != null)
            {
                invoice.PaidAmount = Math.Max(0, invoice.PaidAmount - refund.Amount);
                invoice.Status = invoice.PaidAmount switch
                {
                    var p when p >= invoice.TotalAmount => InvoiceStatuses.Paid,
                    var p when p > 0 => InvoiceStatuses.PartiallyPaid,
                    _ => InvoiceStatuses.Pending
                };
            }
        }

        try
        {
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Refund #{id} settled.";
        }
        catch (DbUpdateConcurrencyException)
        {
            // Invoice.RowVersion / Payment.RowVersion — a concurrent KNET
            // callback or approval moved the row under us.
            TempData["ErrorMessage"] = "Refund or its invoice was changed by someone else — refresh and retry.";
        }
        return RedirectToAction(nameof(Index));
    }
}
