using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Core.Enums;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Infrastructure.Services;

/// <summary>
/// Background jobs registered with Hangfire (see Program.cs schedule below).
/// Each method is idempotent — running it twice on the same day must not
/// produce duplicates or double-charge late fees.
/// </summary>
public class RecurringJobs
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notifications;
    private readonly IExecutiveDashboardComposer _composer;
    private readonly IPdfService _pdf;
    private readonly IEmailService _email;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _config;
    private readonly ILogger<RecurringJobs> _logger;
    private readonly IClock _clock;

    public RecurringJobs(
        ApplicationDbContext context,
        INotificationService notifications,
        IExecutiveDashboardComposer composer,
        IPdfService pdf,
        IEmailService email,
        Microsoft.Extensions.Configuration.IConfiguration config,
        ILogger<RecurringJobs> logger,
        IClock clock)
    {
        _context = context;
        _notifications = notifications;
        _composer = composer;
        _pdf = pdf;
        _email = email;
        _config = config;
        _logger = logger;
        _clock = clock;
    }

    /// <summary>
    /// Sprint 4 — Email the Executive Dashboard PDF for *last* month.
    ///
    /// Two modes:
    ///   • `Reports:MonthlyEmailRecipients` configured (semicolon-separated):
    ///     an ops/SuperAdmin mailing list — gets ONE system-wide dashboard.
    ///   • Otherwise: PER COMPANY. Each active company gets a dashboard scoped
    ///     to its own tenants/units, sent only to that company's Admin users.
    ///     (Previously one system-wide dashboard went to every company's
    ///     admins — a cross-tenant data leak.) SuperAdmins (CompanyId null)
    ///     get the system-wide view.
    /// Schedule: 1st of every month at 06:00 UTC (wired in Program.cs).
    /// </summary>
    public async Task SendMonthlyExecutiveDashboardAsync()
    {
        var today = _clock.Today;
        var lastMonthEnd = new DateTime(today.Year, today.Month, 1).AddDays(-1);
        var lastMonthStart = new DateTime(lastMonthEnd.Year, lastMonthEnd.Month, 1);

        var configured = (_config["Reports:MonthlyEmailRecipients"] ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var ok = 0; var failed = 0;

        if (configured.Count > 0)
        {
            // Ops mailing list → system-wide figures (existing behaviour).
            var model = await _composer.BuildAsync(lastMonthStart, lastMonthEnd);
            (var o, var f) = await SendDashboardAsync(model, configured, lastMonthStart);
            ok += o; failed += f;
        }
        else
        {
            // Admin users grouped by company. Job runs as system → no company
            // filter on Users; IgnoreQueryFilters for safety should one be added.
            var admins = await _context.Users
                .IgnoreQueryFilters()
                .Where(u => u.IsActive && (u.Role == "Admin" || u.Role == "SuperAdmin"))
                .Where(u => u.Email != null && u.Email.Contains("@"))
                .Select(u => new { u.CompanyId, u.Email, u.Role })
                .ToListAsync();

            // Per-company dashboards: only that company's Admins see its numbers.
            var companies = await _context.Companies
                .Where(c => c.IsActive)
                .Select(c => new { c.CompanyId, c.CompanyName })
                .ToListAsync();

            foreach (var company in companies)
            {
                var recipients = admins
                    .Where(a => a.CompanyId == company.CompanyId && a.Role == "Admin")
                    .Select(a => a.Email!)
                    .Distinct()
                    .ToList();
                if (recipients.Count == 0) continue;

                try
                {
                    var model = await _composer.BuildAsync(lastMonthStart, lastMonthEnd, company.CompanyId);
                    (var o, var f) = await SendDashboardAsync(model, recipients, lastMonthStart);
                    ok += o; failed += f;
                }
                catch (Exception ex)
                {
                    // One company's failure must not stop the others.
                    failed += recipients.Count;
                    _logger.LogError(ex, "MonthlyExecutiveDashboard: company {CompanyId} ({Name}) failed",
                        company.CompanyId, company.CompanyName);
                }
            }

            // SuperAdmins (global, CompanyId == null) get the system-wide view.
            var superAdmins = admins
                .Where(a => a.Role == "SuperAdmin")
                .Select(a => a.Email!)
                .Distinct()
                .ToList();
            if (superAdmins.Count > 0)
            {
                var model = await _composer.BuildAsync(lastMonthStart, lastMonthEnd);
                (var o, var f) = await SendDashboardAsync(model, superAdmins, lastMonthStart);
                ok += o; failed += f;
            }
        }

        if (ok == 0 && failed == 0)
        {
            _logger.LogInformation("Job: MonthlyExecutiveDashboard skipped — no recipients configured");
            return;
        }

        _logger.LogInformation(
            "Job: MonthlyExecutiveDashboard sent {Ok} ok / {Failed} failed for period {From:yyyy-MM-dd}..{To:yyyy-MM-dd}",
            ok, failed, lastMonthStart, lastMonthEnd);
    }

    /// <summary>Render one dashboard model to PDF and email it to each recipient.</summary>
    private async Task<(int ok, int failed)> SendDashboardAsync(
        ExecutiveDashboardPdfModel model, IReadOnlyList<string> recipients, DateTime periodStart)
    {
        var pdfBytes = _pdf.RenderExecutiveDashboard(model);
        var fileName = $"executive-dashboard-{periodStart:yyyy-MM}.pdf";
        var subject = $"VIIJAR — Executive Dashboard, {periodStart:MMMM yyyy}";
        var deltaStr = model.CollectedDelta == 0
            ? ""
            : $" ({(model.CollectedDelta > 0 ? "+" : "")}{model.CollectedDelta:0.0}% vs prior period)";
        var body = $"<p>The monthly executive dashboard for <strong>{periodStart:MMMM yyyy}</strong> is attached.</p>" +
                   $"<p>Collected: <strong>{model.Collected:N3} {model.Currency}</strong>{deltaStr}<br>" +
                   $"Outstanding: <strong>{model.Outstanding:N3} {model.Currency}</strong><br>" +
                   $"Overdue invoices: <strong>{model.OverdueCount}</strong><br>" +
                   $"Occupancy: <strong>{model.OccupancyPercent:0.0}%</strong> ({model.OccupiedUnits}/{model.TotalUnits} units)</p>" +
                   "<p>Open the admin portal for the full interactive view.</p>";
        var attachment = new EmailAttachment(fileName, pdfBytes, "application/pdf");

        var ok = 0; var failed = 0;
        foreach (var to in recipients)
        {
            var result = await _email.SendAsync(to, subject, body, new[] { attachment });
            if (result.Success) ok++; else failed++;
            if (!result.Success)
                _logger.LogWarning("MonthlyExecutiveDashboard: send to {To} failed — {Err}", to, result.ErrorMessage);
        }
        return (ok, failed);
    }

    /// <summary>
    /// Monthly Hangfire entry point. Generates both rent AND insurance
    /// invoices in one run so they share a single transaction and an
    /// operator only needs to "Trigger now" once. Each invoice category
    /// has its own idempotency guard. DisableConcurrentExecution stops an
    /// overlapping "Trigger now" + scheduled run from racing on the same
    /// period (the unique index is the last line of defence, not the first).
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task GenerateMonthlyInvoicesAsync()
    {
        await GenerateRentInvoicesAsync();
        await GenerateInsuranceInvoicesAsync();
    }

    /// <summary>
    /// Insurance invoice generation — same shape as rent but honours each
    /// lease's <c>InsuranceFrequency</c>:
    ///   Monthly  → fires every period
    ///   Annual   → fires once per year, on the month of InsuranceStartDate
    ///   OneTime  → fires exactly once ever (idempotent via NOT EXISTS)
    ///   None     → skipped
    /// Separate invoice rows per category so reports stay clean.
    /// </summary>
    public async Task GenerateInsuranceInvoicesAsync()
    {
        var now = _clock.UtcNow;
        var periodStart = new DateTime(now.Year, now.Month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        _logger.LogInformation("Job: GenerateInsuranceInvoices for period {Start}-{End}", periodStart, periodEnd);

        // Same lease-status rule as rent: a lease renewed early is flipped to
        // "Renewed" immediately but still runs (and still carries insurance)
        // until its LeaseEndDate. Terminated / Pending / Expired are excluded.
        var activeLeases = await _context.Leases
            .IgnoreQueryFilters()
            .Where(l => (l.Status == LeaseStatuses.Active || l.Status == LeaseStatuses.Renewed)
                && l.InsuranceAmount > 0
                && l.InsuranceFrequency != "None"
                && l.LeaseStartDate <= periodEnd
                && l.LeaseEndDate >= periodStart)
            .ToListAsync();

        var created = 0;
        var failedRows = 0;
        foreach (var lease in activeLeases)
        {
            // Per-row resilience: one bad lease (unique-index race with a manual
            // "generate" click, RowVersion conflict, bad data) must not abort the
            // whole batch or leave a poisoned entry in the change tracker.
            Invoice? invoice = null;
            try
            {
                var anchor = lease.InsuranceStartDate ?? lease.LeaseStartDate;

                // Cadence gate — skip the lease if this period isn't a billing period.
                bool isBillingPeriod = lease.InsuranceFrequency switch
                {
                    "Monthly" => true,
                    "Annual"  => anchor.Month == periodStart.Month,
                    "OneTime" => true,  // OneTime fires once; idempotency below blocks repeats
                    _          => false,
                };
                if (!isBillingPeriod) continue;

                // OneTime: skip if ANY insurance invoice already exists for this
                // lease (lifetime check). Monthly/Annual: skip only if one already
                // exists in this period.
                bool exists = lease.InsuranceFrequency == "OneTime"
                    ? await _context.Invoices.IgnoreQueryFilters().AnyAsync(i =>
                        i.LeaseId == lease.LeaseId && i.InvoiceType == "Insurance")
                    : await _context.Invoices.IgnoreQueryFilters().AnyAsync(i =>
                        i.LeaseId == lease.LeaseId
                        && i.InvoiceType == "Insurance"
                        && i.InvoiceDate >= periodStart
                        && i.InvoiceDate <= periodEnd);
                if (exists) continue;

                invoice = new Invoice
                {
                    LeaseId = lease.LeaseId,
                    TenantId = lease.TenantId,
                    InvoiceDate = periodStart,
                    DueDate = RentCollection.Core.Leases.LeaseMath.ComputeDueDate(
                        lease.PaymentDueDay, periodStart.Year, periodStart.Month),
                    TotalAmount = lease.InsuranceAmount,
                    PaidAmount = 0,
                    Status = "Pending",
                    InvoiceType = "Insurance",
                    Notes = $"Insurance premium ({lease.InsuranceFrequency}) for {periodStart:yyyy-MM}",
                };
                _context.Invoices.Add(invoice);
                // Save per row so a failure is isolated to this lease and the
                // notification can reference the real InvoiceId.
                await _context.SaveChangesAsync();
                created++;

                await _notifications.EnqueueTenantAsync(
                    lease.TenantId,
                    subject: $"Insurance premium invoice — {periodStart:MMMM yyyy}",
                    body: $"Your insurance premium of {lease.InsuranceAmount:0.000} KWD for {periodStart:MMMM yyyy} is due.",
                    relatedEntityType: "Invoice",
                    relatedEntityId: invoice.InvoiceId);
            }
            catch (Exception ex)
            {
                failedRows++;
                _logger.LogError(ex,
                    "GenerateInsuranceInvoices: lease {LeaseId} failed for period {Period:yyyy-MM} — skipped, batch continues",
                    lease.LeaseId, periodStart);
                // Detach the failed row so the next SaveChanges doesn't re-throw on it.
                if (invoice != null)
                    _context.Entry(invoice).State = EntityState.Detached;
            }
        }

        _logger.LogInformation("Job: GenerateInsuranceInvoices created {Count} invoices ({Failed} rows failed)",
            created, failedRows);
    }

    /// <summary>
    /// Generate this month's rent invoice for every lease that is running in
    /// the period. Idempotent — checks for an existing invoice in the target
    /// month before creating (UQ_Invoices_Lease_Type_Period backs it at the DB).
    ///
    /// Status rule: Active OR Renewed. Renewal flips the old lease to
    /// "Renewed" the moment the successor is created, but the old lease keeps
    /// running until its LeaseEndDate — previously those remaining months got
    /// no rent invoice at all. Terminated / Pending / Expired never bill.
    /// </summary>
    public async Task GenerateRentInvoicesAsync()
    {
        var now = _clock.UtcNow;
        var periodStart = new DateTime(now.Year, now.Month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        _logger.LogInformation("Job: GenerateMonthlyInvoices for period {Start}-{End}", periodStart, periodEnd);

        var activeLeases = await _context.Leases
            .IgnoreQueryFilters()  // job runs as system, sees all companies
            .Where(l => (l.Status == LeaseStatuses.Active || l.Status == LeaseStatuses.Renewed)
                && l.LeaseStartDate <= periodEnd
                && l.LeaseEndDate >= periodStart)
            .ToListAsync();

        // Build the batch first (dedupe per lease), then save ONCE. A save per
        // row looked safer but is O(N²): every SaveChanges re-scans the whole
        // change tracker, so 5,000 leases took minutes (the Scale tests hung).
        // Resilience is kept by falling back to row-by-row ONLY if the batch
        // save throws — that isolates the offending lease(s) and the rest still
        // land.
        var toCreate = new List<(Lease lease, Invoice invoice)>();
        foreach (var lease in activeLeases)
        {
            var exists = await _context.Invoices
                .IgnoreQueryFilters()
                .AnyAsync(i => i.LeaseId == lease.LeaseId
                    && i.InvoiceDate >= periodStart
                    && i.InvoiceDate <= periodEnd
                    && i.InvoiceType == "Rent");
            if (exists) continue;

            toCreate.Add((lease, new Invoice
            {
                LeaseId = lease.LeaseId,
                TenantId = lease.TenantId,
                InvoiceDate = periodStart,
                // LEASE_AUDIT Flaw #6 — was `periodStart.AddDays(PaymentDueDay-1)`
                // which produced 2026-03-03 for Feb + DueDay=31 instead of
                // Feb 28/29. Shared helper clamps correctly.
                DueDate = RentCollection.Core.Leases.LeaseMath.ComputeDueDate(
                    lease.PaymentDueDay, periodStart.Year, periodStart.Month),
                TotalAmount = lease.MonthlyRent,
                PaidAmount = 0,
                Status = "Pending",
                InvoiceType = "Rent",
                Notes = $"Auto-generated rent invoice for {periodStart:yyyy-MM}",
            }));
        }

        var created = 0;
        var failedRows = 0;
        if (toCreate.Count > 0)
        {
            _context.Invoices.AddRange(toCreate.Select(t => t.invoice));
            try
            {
                await _context.SaveChangesAsync(); // fast path — one round-trip
                created = toCreate.Count;
            }
            catch (DbUpdateException batchEx)
            {
                // Slow path: something in the batch violated a constraint (e.g. the
                // unique period index racing a manual "Generate"). Detach all, then
                // save one at a time so only the bad row(s) are skipped.
                _logger.LogWarning(batchEx,
                    "GenerateRentInvoices: batch save failed — isolating per row");
                foreach (var (_, inv) in toCreate)
                    _context.Entry(inv).State = EntityState.Detached;

                foreach (var (lease, inv) in toCreate)
                {
                    try
                    {
                        _context.Invoices.Add(inv);
                        await _context.SaveChangesAsync();
                        created++;
                    }
                    catch (Exception rowEx)
                    {
                        failedRows++;
                        _logger.LogError(rowEx,
                            "GenerateRentInvoices: lease {LeaseId} failed for period {Period:yyyy-MM} — skipped, batch continues",
                            lease.LeaseId, periodStart);
                        _context.Entry(inv).State = EntityState.Detached;
                    }
                }
            }

            // Notify AFTER the invoices are durable (we need their ids). Clear the
            // tracker first so each enqueue's own SaveChanges stays cheap instead
            // of re-scanning thousands of now-Unchanged invoices.
            _context.ChangeTracker.Clear();
            foreach (var (lease, inv) in toCreate)
            {
                if (inv.InvoiceId <= 0) continue; // this row failed above
                try
                {
                    await _notifications.EnqueueTenantAsync(
                        lease.TenantId,
                        subject: $"New rent invoice for {periodStart:MMMM yyyy}",
                        body: $"Your rent of {lease.MonthlyRent:0.000} KWD for {periodStart:MMMM yyyy} is now due.",
                        relatedEntityType: "Invoice",
                        relatedEntityId: inv.InvoiceId);
                }
                catch (Exception nex)
                {
                    // A failed notification must never undo or block invoicing.
                    _logger.LogError(nex,
                        "GenerateRentInvoices: notification enqueue failed for invoice {InvoiceId}", inv.InvoiceId);
                }
            }
        }

        _logger.LogInformation("Job: GenerateMonthlyInvoices created {Count} invoices ({Failed} rows failed)",
            created, failedRows);
    }

    /// <summary>
    /// Flip past-due invoices with an outstanding balance to Overdue.
    /// Covers Pending, Partially Paid AND Pending Verification (a half-paid or
    /// awaiting-approval invoice whose due date passes is still late money —
    /// previously only Pending flipped, so partial payers never aged).
    /// Paid / Cancelled / already-Overdue are untouched. Runs daily at 06:00 UTC.
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task FlipOverdueInvoicesAsync()
    {
        var today = _clock.Today;
        var overdue = await _context.Invoices
            .IgnoreQueryFilters()
            .Where(i => (i.Status == InvoiceStatuses.Pending
                         || i.Status == InvoiceStatuses.PartiallyPaid
                         || i.Status == InvoiceStatuses.PendingVerification)
                        && i.DueDate.Date < today
                        && i.TotalAmount - i.PaidAmount > 0)
            .ToListAsync();

        // Flip the whole batch in ONE save (a save per row is O(N²) over the
        // change tracker and hung the Scale tests). If the batch save fails —
        // typically a RowVersion conflict with a concurrent payment on ONE
        // invoice — fall back to row-by-row so only that invoice is skipped and
        // the rest still age.
        var flipped = 0;
        var flippedOk = new List<Invoice>();
        if (overdue.Count > 0)
        {
            foreach (var inv in overdue) inv.Status = InvoiceStatuses.Overdue;
            try
            {
                await _context.SaveChangesAsync();
                flipped = overdue.Count;
                flippedOk.AddRange(overdue);
            }
            catch (DbUpdateException batchEx)
            {
                _logger.LogWarning(batchEx, "FlipOverdueInvoices: batch save failed — isolating per row");
                // Reset everything to Unchanged so we can retry one at a time.
                foreach (var inv in overdue) _context.Entry(inv).State = EntityState.Detached;
                foreach (var inv in overdue)
                {
                    try
                    {
                        _context.Attach(inv);
                        inv.Status = InvoiceStatuses.Overdue;
                        _context.Entry(inv).Property(i => i.Status).IsModified = true;
                        await _context.SaveChangesAsync();
                        flipped++;
                        flippedOk.Add(inv);
                    }
                    catch (Exception rowEx)
                    {
                        _logger.LogError(rowEx,
                            "FlipOverdueInvoices: invoice {InvoiceId} failed to flip — skipped, batch continues", inv.InvoiceId);
                        _context.Entry(inv).State = EntityState.Detached;
                    }
                }
            }

            // Notify after the flips are durable; clear the tracker so each
            // enqueue's own save stays cheap.
            _context.ChangeTracker.Clear();
            foreach (var inv in flippedOk)
            {
                try
                {
                    await _notifications.EnqueueTenantAsync(
                        inv.TenantId,
                        subject: $"Invoice #{inv.InvoiceId} is overdue",
                        body: $"Your invoice of {inv.TotalAmount:0.000} KWD was due on {inv.DueDate:dd MMM yyyy}. " +
                              $"Outstanding: {(inv.TotalAmount - inv.PaidAmount):0.000} KWD. Please settle as soon as possible.",
                        relatedEntityType: "Invoice",
                        relatedEntityId: inv.InvoiceId);
                }
                catch (Exception nex)
                {
                    _logger.LogError(nex,
                        "FlipOverdueInvoices: notification enqueue failed for invoice {InvoiceId}", inv.InvoiceId);
                }
            }
        }

        _logger.LogInformation("Job: FlipOverdueInvoices flipped {Count} of {Candidates}", flipped, overdue.Count);
    }

    /// <summary>How long a KNET payment may sit in Pending (awaiting its
    /// gateway callback) before it is treated as abandoned. KNET callbacks are
    /// near-instant, so a generous window still catches only truly dead intents.</summary>
    private const int StaleKnetPendingHours = 12;

    /// <summary>Marker appended to Notes when a stale KNET payment is flagged.
    /// Also used as the idempotency check so a payment is flagged once.</summary>
    private const string StaleKnetMarker = "[stale: no KNET callback after 12h — verify with KNET inquiry before failing]";

    /// <summary>
    /// Phase 3 — flag abandoned KNET payments for review. A KNET payment stays
    /// <c>Pending</c> until its gateway callback arrives; if the customer
    /// abandons the hosted page (or the callback is lost) it lingers as Pending
    /// forever and pollutes reconciliation.
    ///
    /// IMPORTANT: this job must NOT set <c>Failed</c>. "No callback arrived" is
    /// not evidence that no money moved — KNET callbacks are lost (network,
    /// our outage, browser closed on the redirect) while the card WAS charged.
    /// Marking those Failed hides captured money and invites a double charge.
    /// The <c>PaymentStatus</c> enum has no NeedsReview/Unknown value, so the
    /// payment is left <c>Pending</c> with <c>HasMismatch = true</c> and a
    /// Notes marker; the mismatch queue surfaces it to the operator.
    ///
    /// A KNET transaction inquiry (action=8, by trackid) must be performed
    /// before any of these is failed — captured → credit the invoice; not
    /// found / not captured → then Failed is safe. Rows that already carry a
    /// <c>CheckoutPaymentId</c> or a <c>KNETReconciliation</c> row are hard
    /// settlement evidence and are skipped. Runs daily.
    /// </summary>
    public async Task ExpireStaleKnetPaymentsAsync()
    {
        var cutoff = _clock.UtcNow.AddHours(-StaleKnetPendingHours);
        var stale = await _context.Payments
            .Where(p => p.PaymentType == PaymentType.KNET
                     && p.Status == PaymentStatus.Pending
                     && p.CreatedDate < cutoff
                     && (p.CheckoutPaymentId == null || p.CheckoutPaymentId == "")
                     // Idempotent: skip rows already flagged on a previous run.
                     && (p.Notes == null || !p.Notes.Contains(StaleKnetMarker)))
            .ToListAsync();

        if (stale.Count == 0)
        {
            _logger.LogInformation("Job: ExpireStaleKnetPayments found none.");
            return;
        }

        // Protect any payment that DOES have a bank-settlement reconciliation
        // row — that is hard evidence the money actually moved, so never fail it.
        var staleIds = stale.Select(p => p.PaymentId).ToList();
        var settled = new HashSet<int>(await _context.KNETReconciliations
            .Where(r => staleIds.Contains(r.PaymentId))
            .Select(r => r.PaymentId)
            .ToListAsync());

        var flagged = 0;
        foreach (var p in stale)
        {
            if (settled.Contains(p.PaymentId))
            {
                _logger.LogWarning(
                    "ExpireStaleKnetPayments: payment {PaymentId} is Pending past {Hours}h but has a " +
                    "reconciliation row — left for operator review, not flagged.",
                    p.PaymentId, StaleKnetPendingHours);
                continue;
            }

            // Do NOT fail. Keep Pending, raise the mismatch flag, leave a marker.
            // Status change only after a KNET inquiry (action=8) confirms the
            // transaction was not captured.
            try
            {
                p.HasMismatch = true;
                p.ModifiedDate = _clock.UtcNow;
                p.Notes = (p.Notes ?? string.Empty) +
                    $"\n{StaleKnetMarker} flagged {_clock.UtcNow:yyyy-MM-dd HH:mm} UTC";
                p.MismatchDetails = string.IsNullOrEmpty(p.MismatchDetails)
                    ? "StaleKnetPending: no gateway callback after 12h"
                    : p.MismatchDetails + "; StaleKnetPending: no gateway callback after 12h";
                await _context.SaveChangesAsync();
                flagged++;

                _logger.LogWarning(
                    "ExpireStaleKnetPayments: payment {PaymentId} (TrackId {TrackId}, {Amount:0.000} KWD) has no KNET " +
                    "callback after {Hours}h — flagged for review, left Pending. Run a KNET inquiry (action=8) before failing.",
                    p.PaymentId, p.TransactionReference, p.AmountPaid, StaleKnetPendingHours);
            }
            catch (Exception ex)
            {
                // RowVersion conflict = a callback just landed. Leave it alone.
                _logger.LogWarning(ex, "ExpireStaleKnetPayments: payment {PaymentId} could not be flagged — skipped", p.PaymentId);
                _context.Entry(p).State = EntityState.Detached;
            }
        }

        _logger.LogInformation(
            "Job: ExpireStaleKnetPayments flagged {Flagged} stale KNET payment(s) for review ({Protected} protected by reconciliation).",
            flagged, stale.Count - flagged);
    }

    /// <summary>
    /// Email tenants and admins T-60/T-30/T-7 days before a lease expires so
    /// renewal/termination can be planned. Runs daily.
    /// </summary>
    public async Task LeaseExpiryRemindersAsync()
    {
        var today = _clock.Today;
        int[] horizons = { 60, 30, 7 };

        foreach (var days in horizons)
        {
            var target = today.AddDays(days);
            var leases = await _context.Leases
                .IgnoreQueryFilters()
                .Where(l => l.Status == "Active" && l.LeaseEndDate.Date == target)
                .ToListAsync();

            foreach (var lease in leases)
            {
                await _notifications.EnqueueTenantAsync(
                    lease.TenantId,
                    subject: $"Your lease ends in {days} days",
                    body: $"Lease #{lease.LeaseId} expires on {lease.LeaseEndDate:dd MMM yyyy}. Contact your property manager to renew or terminate.",
                    relatedEntityType: "Lease",
                    relatedEntityId: lease.LeaseId);
            }
        }
    }

    /// <summary>
    /// LEASE_AUDIT Flaw #7 — daily job that flips Active leases whose end
    /// date has passed to Expired. Without this the Index view used a
    /// client-side override to display "Expired", which disagreed with the
    /// DB and broke the Status filter. The status flip is now the single
    /// source of truth; the view shows what the DB says.
    /// </summary>
    public async Task FlipExpiredLeasesAsync()
    {
        var today = _clock.Today;
        // EF (not raw SQL) so the model snapshot and any future triggers stay
        // consistent. Volume is bounded (≤ once per lease per ever).
        var stale = await _context.Leases
            .IgnoreQueryFilters()
            .Where(l => l.Status == "Active" && l.LeaseEndDate < today)
            .ToListAsync();

        if (stale.Count == 0)
        {
            _logger.LogInformation("Job: FlipExpiredLeases — nothing to flip.");
            return;
        }

        foreach (var lease in stale)
        {
            lease.Status = "Expired";
            lease.ModifiedDate = _clock.UtcNow;
        }
        await _context.SaveChangesAsync();
        _logger.LogInformation("Job: FlipExpiredLeases flipped {Count} leases to Expired.", stale.Count);
    }

    /// <summary>
    /// Daily Unit.Status reconciliation. Flips:
    ///   • any auto-managed unit with an active lease → Occupied
    ///   • any auto-managed unit without an active lease → Vacant
    /// Operator-set holds (Maintenance / Reserved) are LEFT ALONE — those
    /// are intentional states and must survive job runs. Stamps
    /// AvailableFromDate on units that just became Vacant.
    /// </summary>
    public async Task ReconcileUnitStatusAsync()
    {
        var today = _clock.UtcNow;
        // EF rather than raw SQL so RowVersion + interceptors fire.
        var units = await _context.Units
            .IgnoreQueryFilters()
            .Where(u => u.Status != "Maintenance" && u.Status != "Reserved")
            .ToListAsync();

        var activeLeaseUnitIds = await _context.Leases
            .IgnoreQueryFilters()
            .Where(l => l.Status == "Active")
            .Select(l => l.UnitId)
            .Distinct()
            .ToListAsync();
        var activeSet = new HashSet<int>(activeLeaseUnitIds);

        int flipped = 0;
        foreach (var unit in units)
        {
            var desired = activeSet.Contains(unit.UnitId) ? "Occupied" : "Vacant";
            if (unit.Status == desired) continue;

            unit.Status = desired;
            unit.ModifiedDate = today;
            if (desired == "Vacant" && unit.AvailableFromDate == null)
                unit.AvailableFromDate = today;
            flipped++;
        }

        if (flipped > 0) await _context.SaveChangesAsync();
        _logger.LogInformation("Job: ReconcileUnitStatus flipped {Count} units.", flipped);
    }

    /// <summary>
    /// Drain the notification outbox. Runs every 2 minutes.
    /// DisableConcurrentExecution: two overlapping drains (slow SMTP + the next
    /// tick) would both pick the same Pending rows and double-send.
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ProcessNotificationOutboxAsync()
    {
        var sent = await _notifications.ProcessPendingAsync(maxBatch: 50);
        if (sent > 0)
            _logger.LogInformation("Job: ProcessNotificationOutbox sent {Count}", sent);
    }
}
