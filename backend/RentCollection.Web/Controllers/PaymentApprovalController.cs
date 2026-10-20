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
    private readonly INotificationService _notifications;
    private readonly ILogger<PaymentApprovalController> _logger;

    public PaymentApprovalController(
        ApplicationDbContext context,
        IPaymentEligibilityService eligibility,
        INotificationService notifications,
        ILogger<PaymentApprovalController> logger)
    {
        _context = context;
        _eligibility = eligibility;
        _notifications = notifications;
        _logger = logger;
    }

    // Company scope: Payment has no company query filter. Scope the approval
    // queue + guard the approve/reject actions to the caller's company (via the
    // owning Tenant). SuperAdmin is cross-company by design.
    private int? ScopedCompanyId() =>
        User.IsInRole(Roles.SuperAdmin) ? null
        : int.TryParse(User.FindFirst("companyId")?.Value, out var cid) ? cid : -1;

    private bool CanAccessCompany(int? entityCompanyId)
    {
        if (User.IsInRole(Roles.SuperAdmin)) return true;
        return int.TryParse(User.FindFirst("companyId")?.Value, out var cid)
            && entityCompanyId == cid;
    }

    /// <summary>
    /// Badge counts for the approval nav (pending / approved-today /
    /// rejected-today), restricted to manual (Cash / BankTransfer) payments
    /// and scoped to the caller's company like the queue itself.
    /// </summary>
    private async Task SetBadgeCountsAsync(int? scope, int? pendingCount = null)
    {
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        var manual = _context.Payments.Where(p =>
            (p.PaymentType == PaymentType.Cash || p.PaymentType == PaymentType.BankTransfer) &&
            (scope == null || (p.Tenant != null && p.Tenant.CompanyId == scope)));

        ViewBag.PendingCount = pendingCount
            ?? await manual.CountAsync(p => p.Status == PaymentStatus.PendingVerification);
        ViewBag.ApprovedCount = await manual.CountAsync(p =>
            p.Status == PaymentStatus.Completed &&
            p.ApprovedDate >= today && p.ApprovedDate < tomorrow);
        ViewBag.RejectedCount = await manual.CountAsync(p =>
            p.Status == PaymentStatus.Rejected &&
            p.ModifiedDate >= today && p.ModifiedDate < tomorrow);
    }

    // GET: PaymentApproval
    public async Task<IActionResult> Index()
    {
        var scope = ScopedCompanyId();

        // Get pending payments (Cash or BankTransfer with PendingVerification status)
        var pendingPayments = await _context.Payments
            .Include(p => p.Tenant)
            .Include(p => p.Property)
            .Include(p => p.Invoice)
            .Include(p => p.BankTransfer)
            .Include(p => p.CashPayment)
            .Where(p => p.Status == PaymentStatus.PendingVerification &&
                       (p.PaymentType == PaymentType.Cash || p.PaymentType == PaymentType.BankTransfer) &&
                       (scope == null || (p.Tenant != null && p.Tenant.CompanyId == scope)))
            .OrderByDescending(p => p.CreatedDate)
            .ToListAsync();

        await SetBadgeCountsAsync(scope, pendingPayments.Count);

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
        // Company scope — same rule as Approve/Reject.
        if (!CanAccessCompany(payment.Tenant?.CompanyId)) return NotFound();

        await SetBadgeCountsAsync(ScopedCompanyId());

        return View(payment);
    }

    // POST: PaymentApproval/Approve/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, string? manualReceiptNumber = null)
    {
        var payment = await _context.Payments
            .Include(p => p.Tenant)
            .Include(p => p.Invoice)
            .Include(p => p.BankTransfer)
            .Include(p => p.CashPayment)
            .FirstOrDefaultAsync(p => p.PaymentId == id);

        if (payment == null)
        {
            return NotFound();
        }
        // Company scope: don't let a company-scoped admin approve another
        // company's payment.
        if (!CanAccessCompany(payment.Tenant?.CompanyId)) return NotFound();

        // Guard: only a payment still awaiting verification may be approved.
        // Without this, re-POSTing Approve double-credits the invoice, and a
        // Rejected/Completed payment could be (re)approved. (Red-team #1/#2.)
        if (payment.Status != PaymentStatus.PendingVerification)
        {
            TempData["ErrorMessage"] =
                $"Payment #{id} is already {payment.Status} and cannot be approved again.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // Two-person control: whoever recorded the money may not also verify
        // it. Cash is recorded by a staff member (Payment.CollectedBy /
        // CashPayment.CollectedBy); a bank transfer is submitted by
        // BankTransfer.SubmittedBy. Compare case-insensitively against the
        // approver's identity. A tenant-initiated request is recorded as
        // "tenant-request:<id>", so any staff member may verify it.
        //
        // SuperAdmin override: a one-person office would otherwise deadlock
        // (nobody else to approve). The override is allowed but stamped on
        // the payment notes and the audit row so it is visible in History.
        var approver = User.Identity?.Name;
        var recorder = payment.PaymentType == PaymentType.Cash
            ? (string.IsNullOrWhiteSpace(payment.CollectedBy)
                ? payment.CashPayment?.CollectedBy
                : payment.CollectedBy)
            : payment.BankTransfer?.SubmittedBy;
        var isSelfApproval = !string.IsNullOrWhiteSpace(recorder)
            && !string.IsNullOrWhiteSpace(approver)
            && string.Equals(recorder, approver, StringComparison.OrdinalIgnoreCase);
        if (isSelfApproval && !User.IsInRole(Roles.SuperAdmin))
        {
            TempData["ErrorMessage"] = "A different user must verify a payment you recorded.";
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

        // Phase 4 — reject an over-payment instead of silently clamping it.
        // If approving this payment would push PaidAmount past the invoice
        // total, the approver must correct the amount first (the excess is not
        // absorbed and quietly lost).
        if (payment.Invoice != null &&
            payment.Invoice.PaidAmount + payment.AmountPaid > payment.Invoice.TotalAmount)
        {
            var remaining = payment.Invoice.TotalAmount - payment.Invoice.PaidAmount;
            TempData["ErrorMessage"] =
                $"Cannot approve payment #{id}: KWD {payment.AmountPaid:0.000} exceeds the remaining " +
                $"balance of KWD {remaining:0.000} on invoice #{payment.Invoice.InvoiceId}. " +
                "Correct the payment amount, then approve.";
            return RedirectToAction(nameof(Details), new { id });
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
            // Counter staff type the receipt-book number at confirmation time
            // for app-originated cash requests (nobody had it at request time).
            if (string.IsNullOrWhiteSpace(payment.CashPayment.ManualReceiptNumber)
                && !string.IsNullOrWhiteSpace(manualReceiptNumber))
            {
                var n = manualReceiptNumber.Trim();
                payment.CashPayment.ManualReceiptNumber = n.Length > 50 ? n[..50] : n;
            }
        }

        // Update invoice PaidAmount if there's an associated invoice
        if (payment.Invoice != null)
        {
            payment.Invoice.PaidAmount += payment.AmountPaid;
            // Defense-in-depth against concurrent approvals over-crediting an
            // invoice (red-team #4): never let PaidAmount exceed the total.
            if (payment.Invoice.PaidAmount > payment.Invoice.TotalAmount)
                payment.Invoice.PaidAmount = payment.Invoice.TotalAmount;

            if (payment.Invoice.PaidAmount >= payment.Invoice.TotalAmount)
            {
                payment.Invoice.Status = InvoiceStatuses.Paid;
            }
            else if (payment.Invoice.PaidAmount > 0)
            {
                payment.Invoice.Status = InvoiceStatuses.PartiallyPaid;
            }
        }

        if (isSelfApproval)
        {
            var stamp = $"[SELF-APPROVED by {adminName} (SuperAdmin override) at {DateTime.UtcNow:yyyy-MM-dd HH:mm}]";
            payment.Notes = string.IsNullOrEmpty(payment.Notes) ? stamp : $"{payment.Notes}\n{stamp}";
            _logger.LogWarning("Payment {PaymentId} self-approved by SuperAdmin {User}", payment.PaymentId, adminName);
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
            Notes = isSelfApproval
                ? $"{payment.PaymentType} payment SELF-APPROVED by {adminName} (SuperAdmin override — recorder and approver are the same user)"
                : $"{payment.PaymentType} payment approved by {adminName}"
        });

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Payment.RowVersion / Invoice.RowVersion — a concurrent approval,
            // rejection or KNET callback moved the row. Nothing was written.
            TempData["ErrorMessage"] =
                $"Payment #{id} was changed by someone else — refresh and retry.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await NotifyTenantAsync(payment, approved: true, reason: null);

        TempData["SuccessMessage"] = $"Payment #{id} has been approved successfully.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Tell the tenant the outcome of a manual (cash / bank transfer)
    /// verification. Push goes to every registered device; email only when
    /// the tenant actually has an address (most don't — an empty recipient
    /// just fails in the outbox). Runs AFTER the money state is committed and
    /// never fails the approval: a notification problem is logged, not surfaced.
    /// </summary>
    private async Task NotifyTenantAsync(Payment payment, bool approved, string? reason)
    {
        try
        {
            var kind = payment.PaymentType == PaymentType.Cash ? "cash payment" : "bank transfer";
            var invoiceRef = payment.InvoiceId is int invoiceId ? $" for invoice #{invoiceId}" : string.Empty;
            var amount = $"KWD {payment.AmountPaid:0.000}";

            string title, body;
            if (approved)
            {
                var receipt = payment.ReceiptNumber ?? $"RCPT-{payment.PaymentId:D8}";
                title = "Payment confirmed";
                body = $"Your {kind} of {amount}{invoiceRef} has been confirmed. Receipt {receipt}. Thank you.";
            }
            else
            {
                title = "Payment not confirmed";
                body = $"Your {kind} of {amount}{invoiceRef} could not be confirmed. Reason: {reason}. " +
                       "Please contact the office or submit it again.";
            }

            var data = new Dictionary<string, string>
            {
                ["type"] = approved ? "payment_approved" : "payment_rejected",
                ["paymentId"] = payment.PaymentId.ToString(),
            };
            if (payment.InvoiceId is int iid) data["invoiceId"] = iid.ToString();

            await _notifications.EnqueueTenantPushAsync(
                payment.TenantId, title, body, data,
                relatedEntityType: "Payment", relatedEntityId: payment.PaymentId);

            if (!string.IsNullOrWhiteSpace(payment.Tenant?.Email))
            {
                await _notifications.EnqueueTenantAsync(
                    payment.TenantId, title, body,
                    relatedEntityType: "Payment", relatedEntityId: payment.PaymentId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to enqueue {Outcome} notification for payment {PaymentId} (tenant {TenantId})",
                approved ? "approval" : "rejection", payment.PaymentId, payment.TenantId);
        }
    }

    // POST: PaymentApproval/Reject/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string reason)
    {
        var payment = await _context.Payments
            .Include(p => p.Tenant)
            .Include(p => p.BankTransfer)
            .Include(p => p.CashPayment)
            .Include(p => p.Invoice)
            .FirstOrDefaultAsync(p => p.PaymentId == id);

        if (payment == null)
        {
            return NotFound();
        }
        if (!CanAccessCompany(payment.Tenant?.CompanyId)) return NotFound();

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

        // A rejection without a reason is unauditable and tells the tenant
        // nothing about what to fix on resubmission.
        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["ErrorMessage"] = "A rejection reason is required.";
            return RedirectToAction(nameof(Details), new { id });
        }
        reason = reason.Trim();

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

        // Put the invoice back into the state it would be in without this
        // payment — NOT a blind reset to Pending, which hid partial payments
        // and un-flagged overdue invoices until the nightly job ran again.
        if (payment.Invoice != null && payment.Invoice.Status == InvoiceStatuses.PendingVerification)
        {
            var today = DateTime.UtcNow.Date;
            payment.Invoice.Status =
                payment.Invoice.PaidAmount > 0 ? InvoiceStatuses.PartiallyPaid
                : payment.Invoice.DueDate.Date < today ? InvoiceStatuses.Overdue
                : InvoiceStatuses.Pending;
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

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["ErrorMessage"] =
                $"Payment #{id} was changed by someone else — refresh and retry.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await NotifyTenantAsync(payment, approved: false, reason);

        TempData["SuccessMessage"] = $"Payment #{id} has been rejected.";
        return RedirectToAction(nameof(Index));
    }

    // GET: PaymentApproval/History
    public async Task<IActionResult> History()
    {
        var scope = ScopedCompanyId();

        // Get all approved and rejected payments (Cash or BankTransfer)
        var historyPayments = await _context.Payments
            .Include(p => p.Tenant)
            .Include(p => p.Property)
            .Include(p => p.Invoice)
            .Include(p => p.BankTransfer)
            .Include(p => p.CashPayment)
            .Where(p => (p.Status == PaymentStatus.Completed || p.Status == PaymentStatus.Rejected) &&
                       (p.PaymentType == PaymentType.Cash || p.PaymentType == PaymentType.BankTransfer) &&
                       (scope == null || (p.Tenant != null && p.Tenant.CompanyId == scope)))
            .OrderByDescending(p => p.ApprovedDate ?? p.ModifiedDate)
            .ToListAsync();

        await SetBadgeCountsAsync(scope);

        return View(historyPayments);
    }
}
