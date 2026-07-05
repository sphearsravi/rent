using Microsoft.EntityFrameworkCore;
using RentCollection.Application.Interfaces;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Infrastructure.Services;

/// <summary>
/// Concrete aggregator behind <see cref="IMonthEndCloseService"/>. The
/// algorithm here is the single source of truth for what each row on the
/// Month-End Close page reports — both the JSON endpoint
/// (<c>ReportsController.MonthEndCloseState</c>) and the page bootstrap
/// (<c>ReportsController.MonthEndClose</c>) go through this service.
///
/// Each of the 9 steps maps to a colour:
///   • green — the work is done
///   • amber — partial / informational (e.g. some bank transfers still pending)
///   • red   — blocking (rent invoices missing — books can't close)
///   • muted — N/A (nothing expected this period)
/// </summary>
public class MonthEndCloseService : IMonthEndCloseService
{
    private readonly ApplicationDbContext _context;
    private readonly IClock _clock;

    public MonthEndCloseService(ApplicationDbContext context, IClock clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<object> BuildCloseStateAsync(string? period)
    {
        var now = _clock.UtcNow;
        int year = now.Year, month = now.Month;
        if (!string.IsNullOrWhiteSpace(period)
            && System.Text.RegularExpressions.Regex.IsMatch(period, @"^\d{4}-\d{2}$"))
        {
            var parts = period.Split('-');
            year = int.Parse(parts[0]);
            month = int.Parse(parts[1]);
        }
        var periodStart = new DateTime(year, month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        var periodLabel = $"{year:0000}-{month:00}";

        // Step 1: rent invoices issued for active leases covering this period
        var activeLeaseIds = await _context.Leases
            .Where(l => l.Status == "Active"
                || (l.LeaseStartDate <= periodEnd && l.LeaseEndDate >= periodStart))
            .Select(l => l.LeaseId)
            .ToListAsync();
        var rentInvoicesIssued = await _context.Invoices
            .CountAsync(i => i.InvoiceType == "Rent"
                && i.InvoiceDate >= periodStart && i.InvoiceDate <= periodEnd);
        var rentExpected = activeLeaseIds.Count;
        var rentStatus = rentExpected == 0 ? "muted"
            : rentInvoicesIssued >= rentExpected ? "green" : "red";

        // Step 2: deposit invoices issued for any lease that's currently Active
        // and has SecurityDeposit > 0. Pending leases haven't activated yet,
        // so they're not "expected" until they do.
        var depositsExpected = await _context.Leases
            .CountAsync(l => l.Status == "Active" && l.SecurityDeposit > 0);
        var depositsIssued = await _context.Leases
            .CountAsync(l => l.Status == "Active" && l.SecurityDeposit > 0 && l.DepositInvoiced);
        var depositStatus = depositsExpected == 0 ? "muted"
            : depositsIssued >= depositsExpected ? "green" : "amber";
        var pendingActivations = await _context.Leases.CountAsync(l => l.Status == "Pending");

        // Step 3: insurance invoices issued for active leases with Monthly insurance
        var insuranceExpected = await _context.Leases
            .CountAsync(l => l.Status == "Active"
                && l.InsuranceAmount > 0 && l.InsuranceFrequency == "Monthly");
        var insuranceIssued = await _context.Invoices
            .CountAsync(i => i.InvoiceType == "Insurance"
                && i.InvoiceDate >= periodStart && i.InvoiceDate <= periodEnd);
        var insuranceStatus = insuranceExpected == 0 ? "muted"
            : insuranceIssued >= insuranceExpected ? "green" : "amber";

        // Step 4 + 5: leftover work from the daily flip jobs. Both should be
        // zero if FlipOverdueInvoicesAsync / FlipExpiredLeasesAsync ran today.
        var overdueLeftover = await _context.Invoices
            .CountAsync(i => i.Status == "Pending" && i.DueDate.Date < now.Date);
        var expiredLeftover = await _context.Leases
            .CountAsync(l => l.Status == "Active" && l.LeaseEndDate.Date < now.Date);

        // Step 6: bank-transfer approval queue
        var pendingTransfers = await _context.BankTransfers
            .CountAsync(b => b.Status == RentCollection.Core.Enums.BankTransferStatus.PendingVerification);

        // Step 7: K-NET batch reconciled — last batch with status='Matched' or any
        // reconciliation row in this period.
        var lastKnetReconciliation = await _context.KNETReconciliations
            .Where(k => k.TransactionDate >= periodStart && k.TransactionDate <= periodEnd)
            .OrderByDescending(k => k.TransactionDate)
            .Select(k => new { k.TransactionDate, k.ReconciliationStatus, k.ReconciledDate })
            .FirstOrDefaultAsync();
        var knetStatus = lastKnetReconciliation == null ? "muted"
            : lastKnetReconciliation.ReconciliationStatus == "Matched" ? "green"
            : lastKnetReconciliation.ReconciliationStatus == "Pending" ? "amber"
            : "red";

        // Step 8: executive dashboard emailed — check the AuditLog. Falling back:
        // if there's no audit row but we're past the 2nd, mark amber.
        var dashboardEmailSent = await _context.AuditLogs
            .Where(a => a.Action == "MonthlyDashboardEmailed"
                && a.At >= periodStart && a.At <= periodEnd.AddDays(2))
            .OrderByDescending(a => a.At)
            .Select(a => (DateTime?)a.At)
            .FirstOrDefaultAsync();
        var dashboardStatus = dashboardEmailSent.HasValue ? "green"
            : (now > periodStart.AddDays(2) ? "amber" : "muted");

        // Step 9: GL export run history — not currently persisted, so muted.
        var glExportPresent = false;
        var glStatus = glExportPresent ? "green" : "muted";

        // Period closure check
        var closed = await _context.ClosedPeriods
            .Where(c => c.Period == periodLabel)
            .Select(c => new { c.ClosedAt, c.ClosedBy, c.Notes })
            .FirstOrDefaultAsync();

        // Summary tallies
        var allStatuses = new[]
        {
            rentStatus, depositStatus, insuranceStatus,
            overdueLeftover == 0 ? "green" : "amber",
            expiredLeftover == 0 ? "green" : "amber",
            pendingTransfers == 0 ? "green" : "amber",
            knetStatus, dashboardStatus, glStatus,
        };
        int Count(string s) => allStatuses.Count(x => x == s);

        return new
        {
            period = periodLabel,
            periodStart,
            periodEnd,
            closed = closed != null ? new
            {
                closed.ClosedAt,
                closed.ClosedBy,
                closed.Notes,
            } : null,
            steps = new
            {
                rentInvoices = new
                {
                    status = rentStatus,
                    issued = rentInvoicesIssued,
                    expected = rentExpected,
                    triggerJobId = RecurringJobIds.GenerateMonthlyInvoices,
                    blockerHref = rentStatus == "red" ? "/Leases" : null,
                },
                depositInvoices = new
                {
                    status = depositStatus,
                    issued = depositsIssued,
                    expected = depositsExpected,
                    pendingActivations,
                    blockerHref = depositStatus == "amber" ? "/Leases" : null,
                },
                insuranceInvoices = new
                {
                    status = insuranceStatus,
                    issued = insuranceIssued,
                    expected = insuranceExpected,
                    triggerJobId = RecurringJobIds.GenerateMonthlyInvoices,
                },
                overdueFlip = new
                {
                    status = overdueLeftover == 0 ? "green" : "amber",
                    leftover = overdueLeftover,
                    triggerJobId = RecurringJobIds.FlipOverdueInvoices,
                    blockerHref = overdueLeftover > 0 ? "/Invoices?status=Pending" : null,
                },
                expiredLeaseFlip = new
                {
                    status = expiredLeftover == 0 ? "green" : "amber",
                    leftover = expiredLeftover,
                    triggerJobId = RecurringJobIds.FlipExpiredLeases,
                    blockerHref = expiredLeftover > 0 ? "/Leases" : null,
                },
                bankTransferApprovals = new
                {
                    status = pendingTransfers == 0 ? "green" : "amber",
                    pending = pendingTransfers,
                    blockerHref = pendingTransfers > 0 ? "/PaymentApproval" : null,
                },
                knetReconciliation = new
                {
                    status = knetStatus,
                    lastBatchDate = lastKnetReconciliation?.TransactionDate,
                    lastBatchStatus = lastKnetReconciliation?.ReconciliationStatus,
                },
                executiveDashboardEmail = new
                {
                    status = dashboardStatus,
                    lastSentAt = dashboardEmailSent,
                    triggerJobId = RecurringJobIds.MonthlyExecutiveDashboardEmail,
                },
                glExport = new
                {
                    status = glStatus,
                    actionHref = $"/GlExport?start={periodStart:yyyy-MM-dd}&end={periodEnd:yyyy-MM-dd}",
                },
            },
            summary = new
            {
                green = Count("green"),
                amber = Count("amber"),
                red = Count("red"),
                muted = Count("muted"),
                total = allStatuses.Length,
            },
        };
    }
}
