using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Application.Interfaces;
using RentCollection.Infrastructure.Data;
using RentCollection.Core.Entities;
using RentCollection.Core.Enums;

namespace RentCollection.Web.Controllers;

[Authorize(Roles = Roles.Financial)]
public class PaymentApprovalController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IPaymentEligibilityService _eligibility;

    public PaymentApprovalController(ApplicationDbContext context, IPaymentEligibilityService eligibility)
    {
        _context = context;
        _eligibility = eligibility;
    }

    // GET: PaymentApproval
    public async Task<IActionResult> Index()
    {
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        // Get pending payments (Cash or BankTransfer with PendingVerification status)
        var pendingPayments = await _context.Payments
            .Include(p => p.Tenant)
            .Include(p => p.Property)
            .Include(p => p.Invoice)
            .Include(p => p.BankTransfer)
            .Include(p => p.CashPayment)
            .Where(p => p.Status == PaymentStatus.PendingVerification &&
                       (p.PaymentType == PaymentType.Cash || p.PaymentType == PaymentType.BankTransfer))
            .OrderByDescending(p => p.CreatedDate)
            .ToListAsync();

        // Calculate counts for ViewBag
        ViewBag.PendingCount = pendingPayments.Count;

        ViewBag.ApprovedCount = await _context.Payments
            .Where(p => p.Status == PaymentStatus.Completed &&
                       p.ApprovedDate >= today && p.ApprovedDate < tomorrow &&
                       (p.PaymentType == PaymentType.Cash || p.PaymentType == PaymentType.BankTransfer))
            .CountAsync();

        ViewBag.RejectedCount = await _context.Payments
            .Where(p => p.Status == PaymentStatus.Rejected &&
                       p.ModifiedDate >= today && p.ModifiedDate < tomorrow &&
                       (p.PaymentType == PaymentType.Cash || p.PaymentType == PaymentType.BankTransfer))
            .CountAsync();

        return View(pendingPayments);
    }

    // GET: PaymentApproval/Details/5
    public async Task<IActionResult> Details(int id)
    {
        var payment = await _context.Payments
            .Include(p => p.Tenant)
            .Include(p => p.Property)
            .Include(p => p.Invoice)
            .Include(p => p.Lease)
                .ThenInclude(l => l.Unit)
            .Include(p => p.BankTransfer)
            .Include(p => p.CashPayment)
            .FirstOrDefaultAsync(p => p.PaymentId == id);

        if (payment == null)
        {
            return NotFound();
        }

        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        // Calculate counts for ViewBag
        ViewBag.PendingCount = await _context.Payments
            .Where(p => p.Status == PaymentStatus.PendingVerification &&
                       (p.PaymentType == PaymentType.Cash || p.PaymentType == PaymentType.BankTransfer))
            .CountAsync();

        ViewBag.ApprovedCount = await _context.Payments
            .Where(p => p.Status == PaymentStatus.Completed &&
                       p.ApprovedDate >= today && p.ApprovedDate < tomorrow &&
                       (p.PaymentType == PaymentType.Cash || p.PaymentType == PaymentType.BankTransfer))
            .CountAsync();

        ViewBag.RejectedCount = await _context.Payments
            .Where(p => p.Status == PaymentStatus.Rejected &&
                       p.ModifiedDate >= today && p.ModifiedDate < tomorrow &&
                       (p.PaymentType == PaymentType.Cash || p.PaymentType == PaymentType.BankTransfer))
            .CountAsync();

        return View(payment);
    }

    // POST: PaymentApproval/Approve/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        var payment = await _context.Payments
            .Include(p => p.Invoice)
            .Include(p => p.BankTransfer)
            .Include(p => p.CashPayment)
            .FirstOrDefaultAsync(p => p.PaymentId == id);

        if (payment == null)
        {
            return NotFound();
        }

        // Guard: only a payment still awaiting verification may be approved.
        // Without this, re-POSTing Approve double-credits the invoice, and a
        // Rejected/Completed payment could be (re)approved. (Red-team #1/#2.)
        if (payment.Status != PaymentStatus.PendingVerification)
        {
            TempData["ErrorMessage"] =
                $"Payment #{id} is already {payment.Status} and cannot be approved again.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // Sprint 24 — re-check eligibility AT APPROVAL TIME (not at upload
        // time). A tenant may have submitted a bank transfer for Feb while
        // Jan was still unpaid; if Jan got paid in the interim, this Feb
        // transfer is now eligible. Conversely, a transfer submitted as
        // eligible may have lost its slot to a newer manual cash entry.
        // The rule must hold at the moment money becomes Confirmed.
        if (payment.Invoice != null)
        {
            var eligibility = await _eligibility.CheckAsync(payment.Invoice.InvoiceId);
            if (!eligibility.IsEligible)
            {
                TempData["ErrorMessage"] =
                    $"Cannot approve payment #{id}. The tenant has an older outstanding invoice " +
                    $"(#{eligibility.OldestUnpaidInvoiceId}, KWD {eligibility.OldestBalance:0.000} balance). " +
                    "Approve that invoice's payment first, or reject this one.";
                return RedirectToAction(nameof(Details), new { id });
            }
        }

        var adminName = User.Identity?.Name ?? "Admin";

        // Update payment status
        payment.Status = PaymentStatus.Completed;
        payment.ApprovedBy = adminName;
        payment.ApprovedDate = DateTime.UtcNow;
        payment.ModifiedDate = DateTime.UtcNow;

        // Update bank transfer status if applicable
        if (payment.BankTransfer != null)
        {
            payment.BankTransfer.Status = BankTransferStatus.Approved;
            payment.BankTransfer.ApprovedBy = adminName;
            payment.BankTransfer.ApprovedDate = DateTime.UtcNow;
        }

        // Update cash payment if applicable
        if (payment.CashPayment != null)
        {
            payment.CashPayment.Notes = (payment.CashPayment.Notes ?? "") +
                $"\n[Approved by {adminName} at {DateTime.UtcNow:yyyy-MM-dd HH:mm}]";
        }

        // Update invoice PaidAmount if there's an associated invoice
        if (payment.Invoice != null)
        {
            payment.Invoice.PaidAmount += payment.AmountPaid;
            // Defense-in-depth against concurrent approvals over-crediting an
            // invoice (red-team #4): never let PaidAmount exceed the total.
            if (payment.Invoice.PaidAmount > payment.Invoice.TotalAmount)
                payment.Invoice.PaidAmount = payment.Invoice.TotalAmount;

            // Status string must match the rest of the codebase
            // ("Partially Paid" with a space — KNET/invoice paths use that).
            if (payment.Invoice.PaidAmount >= payment.Invoice.TotalAmount)
            {
                payment.Invoice.Status = "Paid";
            }
            else if (payment.Invoice.PaidAmount > 0)
            {
                payment.Invoice.Status = "Partially Paid";
            }
        }

        // Audit trail — cash/bank approvals are money events; record who
        // approved, when, and how much (parity with the KNET audit path).
        _context.PaymentAudits.Add(new PaymentAudit
        {
            PaymentId = payment.PaymentId,
            AuditType = "Approved",
            AuditTimestamp = DateTime.UtcNow,
            Amount = payment.AmountPaid,
            AuditedBy = adminName,
            IsMismatch = false,
            IsResolved = true,
            Notes = $"{payment.PaymentType} payment approved by {adminName}"
        });

        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Payment #{id} has been approved successfully.";
        return RedirectToAction(nameof(Index));
    }

    // POST: PaymentApproval/Reject/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string reason)
    {
        var payment = await _context.Payments
            .Include(p => p.BankTransfer)
            .Include(p => p.CashPayment)
            .Include(p => p.Invoice)
            .FirstOrDefaultAsync(p => p.PaymentId == id);

        if (payment == null)
        {
            return NotFound();
        }

        // Guard: only a payment still awaiting verification may be rejected.
        // Rejecting an already-Completed payment would leave the invoice Paid
        // with no valid payment behind it — reverse a completed payment via the
        // refund flow instead. (Red-team #3.)
        if (payment.Status != PaymentStatus.PendingVerification)
        {
            TempData["ErrorMessage"] =
                $"Payment #{id} is {payment.Status} and cannot be rejected. Use a refund to reverse a completed payment.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // Update payment status
        payment.Status = PaymentStatus.Rejected;
        payment.ModifiedDate = DateTime.UtcNow;

        // Add rejection reason to notes
        var rejectionNote = $"[Rejected on {DateTime.UtcNow:yyyy-MM-dd HH:mm} by {User.Identity?.Name ?? "Admin"}] Reason: {reason}";
        payment.Notes = string.IsNullOrEmpty(payment.Notes)
            ? rejectionNote
            : $"{payment.Notes}\n{rejectionNote}";

        // Update bank transfer status if applicable
        if (payment.BankTransfer != null)
        {
            payment.BankTransfer.Status = BankTransferStatus.Rejected;
            payment.BankTransfer.RejectionReason = reason;
        }

        // Update cash payment status if applicable
        if (payment.CashPayment != null)
        {
            payment.CashPayment.Notes = (payment.CashPayment.Notes ?? "") +
                $"\n[Rejected: {reason}]";
        }

        // Revert invoice status to Pending so tenant can resubmit
        if (payment.Invoice != null && payment.Invoice.Status == "Pending Verification")
        {
            payment.Invoice.Status = "Pending";
        }

        _context.PaymentAudits.Add(new PaymentAudit
        {
            PaymentId = payment.PaymentId,
            AuditType = "Rejected",
            AuditTimestamp = DateTime.UtcNow,
            Amount = payment.AmountPaid,
            AuditedBy = User.Identity?.Name ?? "Admin",
            IsMismatch = false,
            IsResolved = true,
            Notes = $"{payment.PaymentType} payment rejected. Reason: {reason}"
        });

        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Payment #{id} has been rejected.";
        return RedirectToAction(nameof(Index));
    }

    // GET: PaymentApproval/History
    public async Task<IActionResult> History()
    {
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        // Get all approved and rejected payments (Cash or BankTransfer)
        var historyPayments = await _context.Payments
            .Include(p => p.Tenant)
            .Include(p => p.Property)
            .Include(p => p.Invoice)
            .Include(p => p.BankTransfer)
            .Include(p => p.CashPayment)
            .Where(p => (p.Status == PaymentStatus.Completed || p.Status == PaymentStatus.Rejected) &&
                       (p.PaymentType == PaymentType.Cash || p.PaymentType == PaymentType.BankTransfer))
            .OrderByDescending(p => p.ApprovedDate ?? p.ModifiedDate)
            .ToListAsync();

        // Calculate counts for ViewBag
        ViewBag.PendingCount = await _context.Payments
            .Where(p => p.Status == PaymentStatus.PendingVerification &&
                       (p.PaymentType == PaymentType.Cash || p.PaymentType == PaymentType.BankTransfer))
            .CountAsync();

        ViewBag.ApprovedCount = await _context.Payments
            .Where(p => p.Status == PaymentStatus.Completed &&
                       p.ApprovedDate >= today && p.ApprovedDate < tomorrow &&
                       (p.PaymentType == PaymentType.Cash || p.PaymentType == PaymentType.BankTransfer))
            .CountAsync();

        ViewBag.RejectedCount = await _context.Payments
            .Where(p => p.Status == PaymentStatus.Rejected &&
                       p.ModifiedDate >= today && p.ModifiedDate < tomorrow &&
                       (p.PaymentType == PaymentType.Cash || p.PaymentType == PaymentType.BankTransfer))
            .CountAsync();

        return View(historyPayments);
    }
}
