using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Core.Entities;
using RentCollection.Core.Enums;
using RentCollection.Infrastructure.Data;
using RentCollection.Web.Models;

namespace RentCollection.Web.Controllers;

public class DashboardController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(ApplicationDbContext context, ILogger<DashboardController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// SECURITY: only Property / Tenant / User / LegalCase carry an EF company
    /// query filter. Units, Leases, Invoices, Payments, MaintenanceRequests,
    /// BankTransfers and AuditLogs do NOT, so every aggregate below is scoped
    /// explicitly through the filtered parent. Returns null for SuperAdmin
    /// (sees all); -1 when the claim is missing so predicates match nothing.
    /// </summary>
    private int? ScopedCompanyId()
    {
        if (User.IsInRole(Roles.SuperAdmin)) return null;
        return int.TryParse(User.FindFirst("companyId")?.Value, out var c) ? c : -1;
    }

    /// <summary>Trend chart bar height: integer px out of 160, proportional to
    /// <paramref name="max"/>. Non-zero values always get at least 2px so a tiny
    /// month doesn't vanish; values above the max are clamped.</summary>
    private static int BarPx(decimal value, decimal max)
    {
        if (value <= 0 || max <= 0) return 0;
        var px = (int)Math.Round(value / max * 160m);
        return Math.Clamp(px, 2, 160);
    }

    public async Task<IActionResult> Index()
    {
        try
        {
            // NOTE: removed a CanConnectAsync() probe here — it was an extra remote
            // round-trip on every dashboard load (and, with no command timeout, could
            // itself hang). If the DB is truly down the first real query below throws
            // and is handled by the same catch.
            var cid = ScopedCompanyId();
            var units = _context.Units.AsNoTracking().AsQueryable();
            var payments = _context.Payments.AsNoTracking().AsQueryable();
            var invoices = _context.Invoices.AsNoTracking().AsQueryable();
            var leases = _context.Leases.AsNoTracking().AsQueryable();
            var maintenance = _context.MaintenanceRequests.AsNoTracking().AsQueryable();
            var bankTransfers = _context.BankTransfers.AsNoTracking().AsQueryable();
            if (cid != null)
            {
                units = units.Where(u => u.Property.CompanyId == cid);
                payments = payments.Where(p => p.Tenant.CompanyId == cid);
                invoices = invoices.Where(i => i.Tenant.CompanyId == cid);
                leases = leases.Where(l => l.Tenant.CompanyId == cid);
                maintenance = maintenance.Where(m => m.Property.CompanyId == cid);
                bankTransfers = bankTransfers.Where(b => b.Tenant.CompanyId == cid);
            }

            var now = DateTime.UtcNow;
            var today = now.Date;
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var nextMonthStart = monthStart.AddMonths(1);
            var trendStart = monthStart.AddMonths(-5);
            var over30Cutoff = today.AddDays(-30);
            var ui = CultureInfo.CurrentUICulture;

            // ── Header ───────────────────────────────────────────────────
            var localHour = DateTime.Now.Hour;
            var greetingKey = localHour < 12 ? "Good morning"
                            : localHour < 17 ? "Good afternoon"
                            : "Good evening";
            var firstName = User.FindFirst(ClaimTypes.GivenName)?.Value;
            if (string.IsNullOrWhiteSpace(firstName)) firstName = User.Identity?.Name;
            string? companyName = null;
            if (cid != null && cid > 0)
            {
                companyName = await _context.Companies.AsNoTracking()
                    .Where(c => c.CompanyId == cid)
                    .Select(c => c.CompanyName)
                    .FirstOrDefaultAsync();
            }

            // ── Money row ────────────────────────────────────────────────
            // Only captured money counts as revenue — pending / failed / refunded
            // payments used to inflate this figure.
            var collectedThisMonth = await payments
                .Where(p => p.PaymentDate >= monthStart && p.PaymentDate < nextMonthStart
                         && p.Status == PaymentStatus.Completed)
                .SumAsync(p => (decimal?)p.AmountPaid) ?? 0m;

            // Expected rent roll per property: this month's Rent invoices, with
            // active-lease rent as the fallback when nothing has been invoiced yet
            // (e.g. before the 1st-of-month Hangfire run).
            var rentInvoicedByProperty = await invoices
                .Where(i => i.InvoiceType == InvoiceTypes.Rent
                         && i.InvoiceDate >= monthStart && i.InvoiceDate < nextMonthStart)
                .GroupBy(i => i.Lease.Unit.PropertyId)
                .Select(g => new { PropertyId = g.Key, Total = g.Sum(i => i.TotalAmount) })
                .ToDictionaryAsync(x => x.PropertyId, x => x.Total);
            var activeRentByProperty = await leases
                .Where(l => l.Status == LeaseStatuses.Active)
                .GroupBy(l => l.Unit.PropertyId)
                .Select(g => new { PropertyId = g.Key, Total = g.Sum(l => l.MonthlyRent) })
                .ToDictionaryAsync(x => x.PropertyId, x => x.Total);
            var expectedThisMonth = rentInvoicedByProperty.Values.Sum();
            if (expectedThisMonth == 0) expectedThisMonth = activeRentByProperty.Values.Sum();

            // Outstanding balance across every still-collectable status, not
            // just "Pending" (Overdue + Partially Paid were previously omitted).
            var outstanding = await invoices
                .Where(i => i.Status == InvoiceStatuses.Pending
                         || i.Status == InvoiceStatuses.Overdue
                         || i.Status == InvoiceStatuses.PartiallyPaid)
                .GroupBy(i => 1)
                .Select(g => new { Count = g.Count(), Amount = g.Sum(i => i.TotalAmount - i.PaidAmount) })
                .FirstOrDefaultAsync();

            // Overdue = flagged Overdue, OR still collectable and past due (the
            // Invoices list derives the same thing at read time, so the two agree
            // before the daily FlipOverdueInvoices job runs). One projection feeds
            // every overdue figure, the arrears list and per-property overdue.
            var overdueRows = await invoices
                .Where(i => (i.Status == InvoiceStatuses.Overdue
                          || ((i.Status == InvoiceStatuses.Pending || i.Status == InvoiceStatuses.PartiallyPaid)
                              && i.DueDate < today))
                         && i.TotalAmount > i.PaidAmount)
                .Select(i => new
                {
                    i.TenantId,
                    i.DueDate,
                    Balance = i.TotalAmount - i.PaidAmount,
                    i.Lease.Unit.PropertyId,
                    i.Lease.Unit.Property.PropertyName,
                    i.Lease.Unit.UnitNumber,
                    i.Tenant.FirstName,
                    i.Tenant.LastName,
                })
                .ToListAsync();

            var overdueAmount = overdueRows.Sum(r => r.Balance);
            var overdueTenantCount = overdueRows.Select(r => r.TenantId).Distinct().Count();
            var over30Rows = overdueRows.Where(r => r.DueDate < over30Cutoff).ToList();
            var overdueOver30Amount = over30Rows.Sum(r => r.Balance);
            var overdueOver30TenantCount = over30Rows.Select(r => r.TenantId).Distinct().Count();

            var topArrears = overdueRows
                .GroupBy(r => r.TenantId)
                .Select(g =>
                {
                    var first = g.First();
                    var oldest = g.OrderBy(r => r.DueDate).First();
                    var fn = (first.FirstName ?? "").Trim();
                    var ln = (first.LastName ?? "").Trim();
                    var initials = string.Concat(
                        fn.Length > 0 ? fn.Substring(0, 1) : "",
                        ln.Length > 0 ? ln.Substring(0, 1) : "");
                    return new ArrearsRow
                    {
                        TenantId = g.Key,
                        TenantName = $"{fn} {ln}".Trim(),
                        Initials = string.IsNullOrEmpty(initials) ? "?" : initials.ToUpperInvariant(),
                        PropertyName = oldest.PropertyName,
                        UnitNumber = oldest.UnitNumber,
                        DaysLate = Math.Max(0, (today - oldest.DueDate.Date).Days),
                        Amount = g.Sum(r => r.Balance),
                    };
                })
                .OrderByDescending(a => a.Amount)
                .Take(5)
                .ToList();

            // Occupancy
            var unitStats = await units
                .GroupBy(u => u.PropertyId)
                .Select(g => new
                {
                    PropertyId = g.Key,
                    Total = g.Count(),
                    Occupied = g.Count(u => u.Status == UnitStatuses.Occupied),
                    VacantRent = g.Where(u => u.Status == UnitStatuses.Vacant).Sum(u => u.MonthlyRent),
                })
                .ToListAsync();
            var totalUnits = unitStats.Sum(s => s.Total);
            var occupiedUnits = unitStats.Sum(s => s.Occupied);
            var vacancyCost = unitStats.Sum(s => s.VacantRent);

            // ── Attention queue ──────────────────────────────────────────
            var toVerify = await payments
                .Where(p => p.Status == PaymentStatus.PendingVerification)
                .GroupBy(p => p.PaymentType)
                .Select(g => new { Type = g.Key, Count = g.Count(), Amount = g.Sum(p => p.AmountPaid) })
                .ToListAsync();

            var maintenanceOpen = await maintenance
                .CountAsync(m => m.Status == "Open" || m.Status == "In Progress");
            var maintenanceSlaBreached = await maintenance
                .CountAsync(m => m.DueDate != null && m.DueDate < now
                              && m.Status != "Completed" && m.Status != "Cancelled");

            var longestVacant = await units
                .Where(u => u.Status == UnitStatuses.Vacant && u.AvailableFromDate != null)
                .OrderBy(u => u.AvailableFromDate)
                .Select(u => new { u.AvailableFromDate, u.UnitNumber, u.Property.PropertyName })
                .FirstOrDefaultAsync();

            // ── Collections trend — last 6 months ────────────────────────
            var expectedByMonth = await invoices
                .Where(i => i.InvoiceType == InvoiceTypes.Rent && i.InvoiceDate >= trendStart)
                .GroupBy(i => new { i.InvoiceDate.Year, i.InvoiceDate.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(i => i.TotalAmount) })
                .ToListAsync();
            var collectedByMonth = await payments
                .Where(p => p.Status == PaymentStatus.Completed && p.PaymentDate >= trendStart)
                .GroupBy(p => new { p.PaymentDate.Year, p.PaymentDate.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(p => p.AmountPaid) })
                .ToListAsync();

            var trend = new List<MonthPoint>(6);
            for (var m = trendStart; m <= monthStart; m = m.AddMonths(1))
            {
                trend.Add(new MonthPoint
                {
                    Year = m.Year,
                    Month = m.Month,
                    Label = m.ToString("MMM", ui),
                    Expected = expectedByMonth.Where(x => x.Year == m.Year && x.Month == m.Month).Sum(x => x.Total),
                    Collected = collectedByMonth.Where(x => x.Year == m.Year && x.Month == m.Month).Sum(x => x.Total),
                    IsCurrent = m == monthStart,
                });
            }
            var trendMax = trend.Max(t => t.Expected);
            if (trendMax <= 0) trendMax = trend.Max(t => t.Collected);
            foreach (var t in trend)
            {
                t.ExpectedPx = BarPx(t.Expected, trendMax);
                t.CollectedPx = BarPx(t.Collected, trendMax);
            }
            var completedMonths = trend.Where(t => !t.IsCurrent && t.Expected > 0).ToList();
            var avgCollectionRate = completedMonths.Count > 0
                ? Math.Round(completedMonths.Average(t => t.Rate), 0) : 0;
            var bestMonth = completedMonths.OrderByDescending(t => t.Rate).FirstOrDefault();

            // Avg days to pay: completed payments against an invoice in the window.
            // Projected to two dates and averaged in memory so this stays
            // provider-agnostic (no DateDiff).
            var payLags = await payments
                .Where(p => p.Status == PaymentStatus.Completed
                         && p.PaymentDate >= trendStart
                         && p.InvoiceId != null)
                .Select(p => new { p.PaymentDate, p.Invoice!.InvoiceDate })
                .ToListAsync();
            var avgDaysToPay = payLags.Count > 0
                ? Math.Round(payLags.Average(x => Math.Max(0, (x.PaymentDate - x.InvoiceDate).TotalDays)), 1)
                : 0;

            // ── Property health ──────────────────────────────────────────
            // Properties carries the company query filter already.
            var propertyNames = await _context.Properties.AsNoTracking()
                .Select(p => new { p.PropertyId, p.PropertyName })
                .ToListAsync();
            var collectedByProperty = await payments
                .Where(p => p.PaymentDate >= monthStart && p.PaymentDate < nextMonthStart
                         && p.Status == PaymentStatus.Completed)
                .GroupBy(p => p.Lease.Unit.PropertyId)
                .Select(g => new { PropertyId = g.Key, Total = g.Sum(p => p.AmountPaid) })
                .ToDictionaryAsync(x => x.PropertyId, x => x.Total);
            var overdueByProperty = overdueRows
                .GroupBy(r => r.PropertyId)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Balance));
            var unitStatsByProperty = unitStats.ToDictionary(s => s.PropertyId);

            var propertyHealth = propertyNames
                .Select(p =>
                {
                    var rentRoll = rentInvoicedByProperty.GetValueOrDefault(p.PropertyId);
                    if (rentRoll == 0) rentRoll = activeRentByProperty.GetValueOrDefault(p.PropertyId);
                    var collected = collectedByProperty.GetValueOrDefault(p.PropertyId);
                    unitStatsByProperty.TryGetValue(p.PropertyId, out var us);
                    return new PropertyHealthRow
                    {
                        PropertyId = p.PropertyId,
                        PropertyName = p.PropertyName,
                        TotalUnits = us?.Total ?? 0,
                        OccupiedUnits = us?.Occupied ?? 0,
                        RentRoll = rentRoll,
                        Collected = collected,
                        CollectionRate = rentRoll > 0
                            ? Math.Min(100, Math.Round((double)(collected / rentRoll) * 100, 0))
                            : 0,
                        OverdueAmount = overdueByProperty.GetValueOrDefault(p.PropertyId),
                    };
                })
                // Weakest first; properties with no rent roll at all sink to the bottom
                // so an empty building doesn't masquerade as a 0% collector.
                .OrderBy(r => r.RentRoll == 0 ? 1 : 0)
                .ThenBy(r => r.CollectionRate)
                .ThenByDescending(r => r.OverdueAmount)
                .Take(10)
                .ToList();

            // ── Lists ────────────────────────────────────────────────────
            var recentPayments = await payments
                .Include(p => p.Tenant)
                .Include(p => p.Lease)
                    .ThenInclude(l => l.Unit)
                        .ThenInclude(u => u.Property)
                .OrderByDescending(p => p.PaymentDate)
                .Take(4)
                .ToListAsync();

            var upcomingExpirations = await leases
                .Include(l => l.Tenant)
                .Include(l => l.Unit)
                    .ThenInclude(u => u.Property)
                .Where(l => l.LeaseEndDate >= now &&
                           l.LeaseEndDate <= now.AddDays(30) &&
                           l.Status == LeaseStatuses.Active)
                .OrderBy(l => l.LeaseEndDate)
                .Take(5)
                .ToListAsync();

            var dashboardData = new DashboardViewModel
            {
                FirstName = firstName,
                CompanyName = companyName,
                GreetingKey = greetingKey,

                TotalProperties = propertyNames.Count,
                TotalUnits = totalUnits,
                OccupiedUnits = occupiedUnits,
                TotalTenants = await _context.Tenants.CountAsync(t => t.Status == TenantStatuses.Active),
                TotalRevenue = collectedThisMonth,
                ExpectedThisMonth = expectedThisMonth,
                DaysLeftInMonth = DateTime.DaysInMonth(now.Year, now.Month) - now.Day,
                PendingPayments = outstanding?.Amount ?? 0m,
                OutstandingInvoiceCount = outstanding?.Count ?? 0,
                OverdueInvoicesCount = overdueRows.Count,
                OverdueAmount = overdueAmount,
                OverdueTenantCount = overdueTenantCount,
                OverdueOver30Amount = overdueOver30Amount,
                OverdueOver30TenantCount = overdueOver30TenantCount,
                VacancyCostPerMonth = vacancyCost,

                PaymentsToVerifyCount = toVerify.Sum(v => v.Count),
                PaymentsToVerifyAmount = toVerify.Sum(v => v.Amount),
                PaymentsToVerifyCashCount = toVerify.Where(v => v.Type == PaymentType.Cash).Sum(v => v.Count),
                PaymentsToVerifyBankCount = toVerify.Where(v => v.Type == PaymentType.BankTransfer).Sum(v => v.Count),
                MaintenanceRequests = maintenanceOpen,
                MaintenanceSlaBreachedCount = maintenanceSlaBreached,
                LongestVacantDays = longestVacant?.AvailableFromDate is DateTime avail && avail < now
                    ? (int)(now - avail).TotalDays : 0,
                LongestVacantUnitLabel = longestVacant == null
                    ? null : $"{longestVacant.PropertyName} U{longestVacant.UnitNumber}",

                CollectionTrend = trend,
                AvgCollectionRate = avgCollectionRate,
                BestMonthLabel = bestMonth?.Label,
                BestMonthRate = bestMonth?.Rate ?? 0,
                AvgDaysToPay = avgDaysToPay,

                PropertyHealth = propertyHealth,
                TopArrears = topArrears,

                RecentPayments = recentPayments,
                UpcomingLeaseExpirations = upcomingExpirations,
                RentRollAtRisk = upcomingExpirations.Sum(l => l.MonthlyRent),

                // ── Action items (kept for the attention queue + other consumers) ──
                LeasesExpiringIn30Days = await leases
                    .CountAsync(l => l.Status == LeaseStatuses.Active
                        && l.LeaseEndDate >= now
                        && l.LeaseEndDate <= now.AddDays(30)),
                PendingLeasesAwaitingActivation = await leases
                    .CountAsync(l => l.Status == LeaseStatuses.Pending),
                PendingPaymentApprovals = await bankTransfers
                    .CountAsync(b => b.Status == BankTransferStatus.PendingVerification),
                DepositsAwaitingPayment = await invoices
                    .CountAsync(i => i.InvoiceType == InvoiceTypes.Deposit
                        && i.Status != InvoiceStatuses.Paid
                        && i.Status != InvoiceStatuses.Cancelled),
                VacantUnitsAvailableSoon = await units
                    .CountAsync(u => u.Status == UnitStatuses.Vacant
                        && (u.AvailableFromDate == null
                            || u.AvailableFromDate <= now.AddDays(30))),
            };

            return View(dashboardData);
        }
        catch (Exception ex)
        {
            // Don't let an all-zero dashboard masquerade as real data: log the
            // failure and flag it so the view can show a "couldn't load" banner.
            _logger.LogError(ex, "Dashboard failed to load; rendering empty model.");
            ViewBag.LoadError = true;
            TempData["ErrorMessage"] = "Dashboard data could not be loaded. The figures below are placeholders — please retry or contact support.";
            var localHour = DateTime.Now.Hour;
            return View(new DashboardViewModel
            {
                FirstName = User.FindFirst(ClaimTypes.GivenName)?.Value ?? User.Identity?.Name,
                GreetingKey = localHour < 12 ? "Good morning" : localHour < 17 ? "Good afternoon" : "Good evening",
                DaysLeftInMonth = DateTime.DaysInMonth(DateTime.UtcNow.Year, DateTime.UtcNow.Month) - DateTime.UtcNow.Day,
                TotalProperties = 0,
                TotalUnits = 0,
                OccupiedUnits = 0,
                TotalTenants = 0,
                TotalRevenue = 0,
                PendingPayments = 0,
                MaintenanceRequests = 0
            });
        }
    }
}
