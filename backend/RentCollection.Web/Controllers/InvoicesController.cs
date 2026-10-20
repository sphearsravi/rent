using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Core.Entities;
using RentCollection.Core.Enums;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.Controllers;

[Authorize(Roles = Roles.Financial)]
public class InvoicesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<InvoicesController> _logger;

    public InvoicesController(ApplicationDbContext context, ILogger<InvoicesController> logger)
    {
        _context = context;
        _logger = logger;
    }

    // Company scope for list queries. SuperAdmin → null (no filter). A scoped
    // admin with NO companyId claim resolves to -1 so the filter matches
    // nothing — previously a missing claim meant "no filter" and the admin saw
    // every company's invoices.
    private int? ScopedCompanyId() =>
        User.IsInRole(Roles.SuperAdmin) ? null
        : int.TryParse(User.FindFirst("companyId")?.Value, out var cid) ? cid : -1;

    public async Task<IActionResult> Index()
    {
        try
        {
            // Company scope (audit C7): Invoice has no company query filter, so
            // this list would otherwise show every company's invoices. Scope to
            // the caller's company via the owning Tenant; SuperAdmin sees all.
            var scope = ScopedCompanyId();
            var q = _context.Invoices.AsNoTracking()
                .Where(i => scope == null || (i.Tenant != null && i.Tenant.CompanyId == scope));

            var invoices = await q
                .Include(i => i.Lease)
                    .ThenInclude(l => l.Unit)
                        .ThenInclude(u => u.Property)
                .Include(i => i.Tenant)
                .OrderByDescending(i => i.InvoiceDate)
                .ToListAsync();

            // Derive "Overdue" at read time so the admin list agrees with the
            // tenant app (which derives it live). The daily FlipOverdueInvoices
            // job persists the same transition — this only closes the window
            // where a past-due invoice still shows "Pending" before that runs.
            // AsNoTracking above guarantees this display-only change is never
            // persisted.
            var today = DateTime.UtcNow.Date;
            foreach (var i in invoices)
                if (i.Status == InvoiceStatuses.Pending && i.DueDate.Date < today)
                    i.Status = InvoiceStatuses.Overdue;

            return View(invoices);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading invoices index");
            TempData["ErrorMessage"] = "Could not load invoices. Please try again.";
            return View(new List<Invoice>());
        }
    }

    // SECURITY (audit C7): Invoice has no company query filter, so a raw
    // InvoiceId lookup is cross-company. [Authorize(Financial)] proves the caller
    // is a finance user, not WHICH company's invoice they may see. Re-check the
    // invoice's company (via its Tenant) against the caller. SuperAdmin is
    // cross-company by design.
    private bool CanAccessCompany(int? entityCompanyId)
    {
        if (User.IsInRole(Roles.SuperAdmin)) return true;
        return int.TryParse(User.FindFirst("companyId")?.Value, out var cid)
            && entityCompanyId == cid;
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var invoice = await _context.Invoices
            .Include(i => i.Lease)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .Include(i => i.Tenant)
            .Include(i => i.Lease)
                .ThenInclude(l => l.Payments)
            .FirstOrDefaultAsync(m => m.InvoiceId == id);

        if (invoice == null)
        {
            return NotFound();
        }
        if (!CanAccessCompany(invoice.Tenant?.CompanyId)) return NotFound();   // audit C7

        return View(invoice);
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var invoice = await _context.Invoices
            .Include(i => i.Lease)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .Include(i => i.Tenant)
            .FirstOrDefaultAsync(m => m.InvoiceId == id);

        if (invoice == null)
        {
            return NotFound();
        }
        if (!CanAccessCompany(invoice.Tenant?.CompanyId)) return NotFound();   // audit C7

        return View(invoice);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var invoice = await _context.Invoices
            .Include(i => i.Tenant)
            .FirstOrDefaultAsync(i => i.InvoiceId == id);
        if (invoice != null)
        {
            if (!CanAccessCompany(invoice.Tenant?.CompanyId)) return NotFound();   // audit C7

            // Money integrity: an invoice that has received money, or that any
            // Payment row (pending, completed or rejected) points at, is part
            // of the ledger and must not disappear. Payments.InvoiceId is also
            // a Restrict FK, so a hard delete would 500 anyway.
            var hasPayments = await _context.Payments.AnyAsync(p => p.InvoiceId == id);
            if (invoice.PaidAmount > 0 || hasPayments)
            {
                TempData["ErrorMessage"] =
                    "This invoice has payments recorded against it and cannot be cancelled. " +
                    "Use a refund to reverse money already collected.";
                return RedirectToAction(nameof(Index));
            }
            if (invoice.Status == InvoiceStatuses.Cancelled)
            {
                TempData["InfoMessage"] = "Invoice is already cancelled.";
                return RedirectToAction(nameof(Index));
            }

            // Soft-cancel instead of hard delete so the invoice number stays in
            // the audit trail and reports can show what was voided.
            invoice.Status = InvoiceStatuses.Cancelled;
            var stamp = $"[Cancelled {DateTime.UtcNow:yyyy-MM-dd HH:mm} by {User.Identity?.Name ?? "system"}]";
            invoice.Notes = string.IsNullOrWhiteSpace(invoice.Notes) ? stamp : $"{invoice.Notes}\n{stamp}";
            try
            {
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Invoice cancelled.";
            }
            catch (DbUpdateConcurrencyException)
            {
                TempData["ErrorMessage"] = "Invoice was changed by someone else — refresh and retry.";
            }
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]   // Red-team #6: block CSRF-triggered invoice generation.
    public async Task<IActionResult> GenerateMonthlyInvoices()
    {
        // NOTE: this is a manual, company-scoped duplicate of the nightly
        // RecurringJobs.GenerateRentInvoicesAsync. The two must agree on
        // InvoiceDate (1st of month), dedupe key (LeaseId + Rent + month) and
        // the lease date-range rule below. Consolidating into one service is
        // tracked separately — not refactored here.
        try
        {
            var currentMonth = DateTime.UtcNow.Month;
            var currentYear = DateTime.UtcNow.Year;
            var periodStart = new DateTime(currentYear, currentMonth, 1);
            var periodEnd = periodStart.AddMonths(1).AddDays(-1);

            // Company scope: a scoped admin only bills their own company's
            // leases (via the owning Tenant). SuperAdmin bills all.
            var scope = ScopedCompanyId();

            // Project only the scalar fields we need, with null-safe casts.
            // Materializing full Lease/Tenant/Unit entities threw
            // SqlNullValueException on demo rows that carry NULLs in
            // non-nullable columns — and since it's one SaveChanges, a single
            // bad row failed the WHOLE batch, so no invoices were generated.
            var activeLeases = await _context.Leases
                .Where(l => l.Status == "Active"
                    && (scope == null || l.Tenant.CompanyId == scope)
                    // Respect the lease period like the job does — don't bill a
                    // month the lease hasn't started or has already ended.
                    && l.LeaseStartDate <= periodEnd
                    && l.LeaseEndDate >= periodStart)
                .Select(l => new
                {
                    l.LeaseId,
                    TenantId = (int?)l.TenantId ?? 0,
                    MonthlyRent = (decimal?)l.MonthlyRent ?? 0m,
                    PaymentDueDay = (int?)l.PaymentDueDay ?? 1,
                    UnitNumber = l.Unit != null ? l.Unit.UnitNumber : null
                })
                .ToListAsync();

            var invoicesGenerated = 0;

            foreach (var lease in activeLeases)
            {
                // Check if a RENT invoice already exists for this month. Must
                // filter by InvoiceType — otherwise a Deposit/Insurance/Other
                // invoice raised this month (e.g. the deposit issued at lease
                // activation) makes this think rent is already billed and skips
                // it. This matches RecurringJobs.GenerateRentInvoicesAsync.
                var existingInvoice = await _context.Invoices
                    .AnyAsync(i => i.LeaseId == lease.LeaseId
                        && i.InvoiceType == InvoiceTypes.Rent
                        && i.InvoiceDate.Month == currentMonth
                        && i.InvoiceDate.Year == currentYear);

                if (!existingInvoice)
                {
                    // LEASE_AUDIT Flaw #6 — clamp moved to LeaseMath so this
                    // controller and RecurringJobs use the exact same rule.
                    var dueDate = RentCollection.Core.Leases.LeaseMath.ComputeDueDate(
                        lease.PaymentDueDay, currentYear, currentMonth);

                    // Create invoice
                    var invoice = new Invoice
                    {
                        LeaseId = lease.LeaseId,
                        TenantId = lease.TenantId,
                        // Normalize to the 1st of the month to match
                        // RecurringJobs.GenerateRentInvoicesAsync. The unique
                        // index (LeaseId, InvoiceType, InvoiceDate) only dedupes
                        // across both generators when both use the same
                        // InvoiceDate — using UtcNow here let the job and this
                        // button each insert a rent invoice for the same month.
                        InvoiceDate = new DateTime(currentYear, currentMonth, 1),
                        DueDate = dueDate,
                        TotalAmount = lease.MonthlyRent,
                        PaidAmount = 0,
                        Status = InvoiceStatuses.Pending,
                        InvoiceType = InvoiceTypes.Rent,
                        Notes = $"Monthly rent for {DateTime.UtcNow:MMMM yyyy} - Unit {lease.UnitNumber}",
                        CreatedDate = DateTime.UtcNow
                    };

                    _context.Invoices.Add(invoice);
                    invoicesGenerated++;
                }
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Generated {invoicesGenerated} invoice(s) for {DateTime.UtcNow:MMMM yyyy}";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            // Never echo ex.Message to the user — it can carry schema / SQL
            // detail. Log it and show a generic message.
            _logger.LogError(ex, "Error generating monthly invoices");
            TempData["ErrorMessage"] = "Error generating invoices. Please try again or contact support.";
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>
    /// Manual settlement. Previously this set PaidAmount = TotalAmount with NO
    /// Payment row — money appeared in the ledger with no record of who took
    /// it, bypassing two-person approval and the PaymentAudit trail. Now:
    /// SuperAdmin-only, and it books a Completed Cash Payment for the remaining
    /// balance plus a "ManualMarkPaid" audit row so payments reconcile to
    /// invoices. Day-to-day settlement must go through Record Cash → Payment
    /// Approval.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]   // audit: money-affecting POST must be CSRF-guarded
    [Authorize(Roles = Roles.SuperAdmin)]
    public async Task<IActionResult> MarkAsPaid(int id)
    {
        // Load the Tenant so we can enforce company scope (Invoice has no
        // company query filter — see CanAccessCompany / audit C7).
        var invoice = await _context.Invoices
            .Include(i => i.Tenant)
            .Include(i => i.Lease).ThenInclude(l => l.Unit)
            .FirstOrDefaultAsync(i => i.InvoiceId == id);
        if (invoice == null) return NotFound();
        if (!CanAccessCompany(invoice.Tenant?.CompanyId)) return NotFound();

        if (invoice.Status == InvoiceStatuses.PendingVerification)
        {
            TempData["ErrorMessage"] =
                "This invoice has a payment awaiting verification. Approve or reject it in Payment Approval first.";
            return RedirectToAction(nameof(Index));
        }
        if (invoice.Status == InvoiceStatuses.Cancelled)
        {
            TempData["ErrorMessage"] = "A cancelled invoice cannot be marked as paid.";
            return RedirectToAction(nameof(Index));
        }
        var remaining = invoice.TotalAmount - invoice.PaidAmount;
        if (invoice.Status == InvoiceStatuses.Paid || remaining <= 0)
        {
            TempData["InfoMessage"] = "Invoice is already paid.";
            return RedirectToAction(nameof(Index));
        }

        var who = User.Identity?.Name ?? "system";
        var now = DateTime.UtcNow;

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var payment = new Payment
            {
                InvoiceId = invoice.InvoiceId,
                LeaseId = invoice.LeaseId,
                TenantId = invoice.TenantId,
                PropertyId = invoice.Lease?.Unit?.PropertyId ?? 0,
                PaymentDate = now,
                AmountPaid = remaining,
                PaymentType = PaymentType.Cash,
                Status = PaymentStatus.Completed,
                CollectedBy = who,
                ApprovedBy = who,
                ApprovedDate = now,
                Notes = $"Manual mark-as-paid by SuperAdmin {who} for invoice #{invoice.InvoiceId}",
                CreatedDate = now,
            };
            _context.Payments.Add(payment);

            invoice.PaidAmount = invoice.TotalAmount;
            invoice.Status = InvoiceStatuses.Paid;

            // First save materialises PaymentId for the audit row.
            await _context.SaveChangesAsync();

            _context.PaymentAudits.Add(new PaymentAudit
            {
                PaymentId = payment.PaymentId,
                AuditType = "ManualMarkPaid",
                AuditTimestamp = now,
                Amount = remaining,
                AuditedBy = who,
                IsMismatch = false,
                IsResolved = true,
                Notes = $"Invoice #{invoice.InvoiceId} manually marked paid by {who}; booked as Cash payment #{payment.PaymentId}"
            });
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["ErrorMessage"] = "Invoice was changed by someone else — refresh and retry.";
            return RedirectToAction(nameof(Index));
        }

        TempData["SuccessMessage"] = $"Invoice marked as paid — {remaining:0.000} KWD booked as a manual cash payment.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]   // audit: status-changing POST must be CSRF-guarded
    public async Task<IActionResult> MarkAsOverdue(int id)
    {
        var invoice = await _context.Invoices
            .Include(i => i.Tenant)
            .FirstOrDefaultAsync(i => i.InvoiceId == id);
        if (invoice != null)
        {
            if (!CanAccessCompany(invoice.Tenant?.CompanyId)) return NotFound();
            // A settled or voided invoice can't be overdue.
            if (invoice.Status == InvoiceStatuses.Paid || invoice.Status == InvoiceStatuses.Cancelled)
            {
                TempData["ErrorMessage"] = $"A {invoice.Status} invoice cannot be marked as overdue.";
                return RedirectToAction(nameof(Index));
            }
            invoice.Status = InvoiceStatuses.Overdue;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Invoice marked as overdue!";
        }

        return RedirectToAction(nameof(Index));
    }
}
