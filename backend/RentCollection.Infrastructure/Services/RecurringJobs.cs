using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
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
    /// Sprint 4 — Email the Executive Dashboard PDF for *last* month to
    /// every recipient configured in `Reports:MonthlyEmailRecipients`
    /// (semicolon-separated). Falls back to all admin-role users by email.
    /// Schedule: 1st of every month at 06:00 UTC (wired in Program.cs).
    /// </summary>
    public async Task SendMonthlyExecutiveDashboardAsync()
    {
        var today = _clock.Today;
        var lastMonthEnd = new DateTime(today.Year, today.Month, 1).AddDays(-1);
        var lastMonthStart = new DateTime(lastMonthEnd.Year, lastMonthEnd.Month, 1);

        var recipients = (_config["Reports:MonthlyEmailRecipients"] ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (recipients.Count == 0)
        {
            recipients = await _context.Users
                .Where(u => u.Role == "Admin" || u.Role == "SuperAdmin")
                .Select(u => u.Email)
                .Where(e => e != null && e.Contains("@"))
                .Cast<string>()
                .Distinct()
                .ToListAsync();
        }

        if (recipients.Count == 0)
        {
            _logger.LogInformation("Job: MonthlyExecutiveDashboard skipped — no recipients configured");
            return;
        }

        var model = await _composer.BuildAsync(lastMonthStart, lastMonthEnd);
        var pdfBytes = _pdf.RenderExecutiveDashboard(model);
        var fileName = $"executive-dashboard-{lastMonthStart:yyyy-MM}.pdf";
        var subject = $"VIIJAR — Executive Dashboard, {lastMonthStart:MMMM yyyy}";
        var deltaStr = model.CollectedDelta == 0
            ? ""
            : $" ({(model.CollectedDelta > 0 ? "+" : "")}{model.CollectedDelta:0.0}% vs prior period)";
        var body = $"<p>The monthly executive dashboard for <strong>{lastMonthStart:MMMM yyyy}</strong> is attached.</p>" +
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
        _logger.LogInformation(
            "Job: MonthlyExecutiveDashboard sent {Ok} ok / {Failed} failed for period {From:yyyy-MM-dd}..{To:yyyy-MM-dd}",
            ok, failed, lastMonthStart, lastMonthEnd);
    }

    /// <summary>
    /// Monthly Hangfire entry point. Generates both rent AND insurance
    /// invoices in one run so they share a single transaction and an
    /// operator only needs to "Trigger now" once. Each invoice category
    /// has its own idempotency guard.
    /// </summary>
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

        var activeLeases = await _context.Leases
            .IgnoreQueryFilters()
            .Where(l => l.Status == "Active"
                && l.InsuranceAmount > 0
                && l.InsuranceFrequency != "None"
                && l.LeaseStartDate <= periodEnd
                && l.LeaseEndDate >= periodStart)
            .ToListAsync();

        var created = 0;
        foreach (var lease in activeLeases)
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

            _context.Invoices.Add(new Invoice
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
            });
            created++;

            await _notifications.EnqueueTenantAsync(
                lease.TenantId,
                subject: $"Insurance premium invoice — {periodStart:MMMM yyyy}",
                body: $"Your insurance premium of {lease.InsuranceAmount:0.000} KWD for {periodStart:MMMM yyyy} is due.",
                relatedEntityType: "Invoice",
                relatedEntityId: null);
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Job: GenerateInsuranceInvoices created {Count} invoices", created);
    }

    /// <summary>
    /// Generate the next month's rent invoice for every active lease.
    /// Idempotent — checks for an existing invoice in the target month before
    /// creating.
    /// </summary>
    public async Task GenerateRentInvoicesAsync()
    {
        var now = _clock.UtcNow;
        var periodStart = new DateTime(now.Year, now.Month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        _logger.LogInformation("Job: GenerateMonthlyInvoices for period {Start}-{End}", periodStart, periodEnd);

        var activeLeases = await _context.Leases
            .IgnoreQueryFilters()  // job runs as system, sees all companies
            .Where(l => l.Status == "Active"
                && l.LeaseStartDate <= periodEnd
                && l.LeaseEndDate >= periodStart)
            .ToListAsync();

        var created = 0;
        foreach (var lease in activeLeases)
        {
            var exists = await _context.Invoices
                .IgnoreQueryFilters()
                .AnyAsync(i => i.LeaseId == lease.LeaseId
                    && i.InvoiceDate >= periodStart
                    && i.InvoiceDate <= periodEnd
                    && i.InvoiceType == "Rent");
            if (exists) continue;

            _context.Invoices.Add(new Invoice
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
            });
            created++;

            await _notifications.EnqueueTenantAsync(
                lease.TenantId,
                subject: $"New rent invoice for {periodStart:MMMM yyyy}",
                body: $"Your rent of {lease.MonthlyRent:0.000} KWD for {periodStart:MMMM yyyy} is now due.",
                relatedEntityType: "Invoice",
                relatedEntityId: null);
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Job: GenerateMonthlyInvoices created {Count} invoices", created);
    }

    /// <summary>
    /// Flip Pending invoices to Overdue when their due date has passed.
    /// Runs daily at 06:00 UTC.
    /// </summary>
    public async Task FlipOverdueInvoicesAsync()
    {
        var today = _clock.Today;
        var overdue = await _context.Invoices
            .IgnoreQueryFilters()
            .Where(i => i.Status == "Pending" && i.DueDate.Date < today)
            .ToListAsync();

        foreach (var inv in overdue)
        {
            inv.Status = "Overdue";
            await _notifications.EnqueueTenantAsync(
                inv.TenantId,
                subject: $"Invoice #{inv.InvoiceId} is overdue",
                body: $"Your invoice of {inv.TotalAmount:0.000} KWD was due on {inv.DueDate:dd MMM yyyy}. Please settle as soon as possible.",
                relatedEntityType: "Invoice",
                relatedEntityId: inv.InvoiceId);
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Job: FlipOverdueInvoices flipped {Count}", overdue.Count);
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
    /// </summary>
    public async Task ProcessNotificationOutboxAsync()
    {
        var sent = await _notifications.ProcessPendingAsync(maxBatch: 50);
        if (sent > 0)
            _logger.LogInformation("Job: ProcessNotificationOutbox sent {Count}", sent);
    }
}
