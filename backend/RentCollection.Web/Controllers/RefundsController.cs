using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Core.Entities;
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

    public async Task<IActionResult> Index()
    {
        var refunds = await _context.Refunds
            .Include(r => r.Payment)
                .ThenInclude(p => p.Tenant)
            .OrderByDescending(r => r.CreatedDate)
            .ToListAsync();
        return View(refunds);
    }

    public async Task<IActionResult> Create(int paymentId)
    {
        var payment = await _context.Payments
            .Include(p => p.Tenant)
            .FirstOrDefaultAsync(p => p.PaymentId == paymentId);
        if (payment == null) return NotFound();
        ViewBag.Payment = payment;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int paymentId, decimal amount, string method, string reason, string? referenceNumber)
    {
        var payment = await _context.Payments.FindAsync(paymentId);
        if (payment == null) return NotFound();
        if (amount <= 0 || amount > payment.AmountPaid)
        {
            TempData["ErrorMessage"] = $"Refund amount must be between 0 and {payment.AmountPaid:0.000}.";
            return RedirectToAction(nameof(Create), new { paymentId });
        }

        _context.Refunds.Add(new Refund
        {
            PaymentId = paymentId,
            Amount = amount,
            Method = method,
            Reason = reason,
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
        var refund = await _context.Refunds.FindAsync(id);
        if (refund == null) return NotFound();
        if (refund.Status != RefundStatus.Pending)
        {
            TempData["ErrorMessage"] = "Only Pending refunds can be approved.";
            return RedirectToAction(nameof(Index));
        }
        refund.Status = RefundStatus.Approved;
        refund.ApprovedBy = User.Identity?.Name;
        refund.ApprovedDate = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.AnyAdmin)]
    public async Task<IActionResult> Settle(int id)
    {
        var refund = await _context.Refunds
            .Include(r => r.Payment)
            .FirstOrDefaultAsync(r => r.RefundId == id);
        if (refund == null) return NotFound();
        if (refund.Status != RefundStatus.Approved)
        {
            TempData["ErrorMessage"] = "Only Approved refunds can be settled.";
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
                    var p when p >= invoice.TotalAmount => "Paid",
                    var p when p > 0 => "Partially Paid",
                    _ => "Pending"
                };
            }
        }

        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
