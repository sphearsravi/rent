using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;
using RentCollection.Core.Enums;
using System.Drawing;

namespace RentCollection.Web.Controllers;

[Authorize(Roles = Roles.Financial)]
public class ReportsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly RentCollection.Application.Interfaces.IPdfService _pdf;
    private readonly RentCollection.Application.Interfaces.IExecutiveDashboardComposer _composer;
    private readonly RentCollection.Application.Interfaces.IMonthEndCloseService _mec;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(
        ApplicationDbContext context,
        RentCollection.Application.Interfaces.IPdfService pdf,
        RentCollection.Application.Interfaces.IExecutiveDashboardComposer composer,
        RentCollection.Application.Interfaces.IMonthEndCloseService mec,
        ILogger<ReportsController> logger)
    {
        _context = context;
        _pdf = pdf;
        _composer = composer;
        _mec = mec;
        _logger = logger;
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    /// <summary>
    /// SECURITY: only Property / Tenant / User / LegalCase carry an EF company
    /// query filter. Units, Leases, Invoices, Payments and MaintenanceRequests
    /// do NOT, so every aggregate in this controller must be scoped explicitly
    /// through the filtered parent (Unit → Property.CompanyId, Lease / Invoice /
    /// Payment → Tenant.CompanyId, MaintenanceRequest → Property.CompanyId).
    /// Returns null for SuperAdmin (sees all); -1 when the claim is missing so
    /// the predicate matches nothing (deny by default).
    /// </summary>
    private int? ScopedCompanyId()
    {
        if (User.IsInRole(Roles.SuperAdmin)) return null;
        return int.TryParse(User.FindFirst("companyId")?.Value, out var c) ? c : -1;
    }

    /// <summary>Exclusive upper bound so the last calendar day of a range is included.</summary>
    private static DateTime EndExclusive(DateTime endDate) => endDate.Date.AddDays(1);

    public IActionResult Index()
    {
        return View();
    }

    #region Month-end close — guided checklist for the 1st-of-the-month sign-off

    /// <summary>
    /// Aggregates 9 close-state signals for the requested period (default =
    /// current month). Each step returns a small object the view turns into
    /// a row with a status tile + action button. Read-only — no mutations.
    /// </summary>
    [Authorize(Roles = Roles.AnyAdmin)]
    public async Task<IActionResult> MonthEndCloseState(string? period)
    {
        var state = await _mec.BuildCloseStateAsync(period);
        return Json(state);
    }

    /// <summary>
    /// HTML version of the same data — the actual checklist page.
    /// </summary>
    [Authorize(Roles = Roles.AnyAdmin)]
    [HttpGet]
    public async Task<IActionResult> MonthEndClose(string? period)
    {
        var state = await _mec.BuildCloseStateAsync(period);
        ViewBag.PeriodState = state;
        return View();
    }

    /// <summary>
    /// Manually trigger a recurring Hangfire job by name. Used by the
    /// "Trigger now" buttons on the close page so an admin can re-run
    /// rent generation, overdue flip, etc. without leaving the checklist.
    /// Returns JSON for inline UI feedback.
    /// </summary>
    // SECURITY: recurring jobs are system-wide (rent generation, overdue flip, …)
    // and have no company dimension, so only a SuperAdmin may fire them.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.SuperAdmin)]
    public IActionResult TriggerJob(string jobId, [FromServices] Hangfire.IRecurringJobManager recurring)
    {
        // Allow-list lives in RecurringJobIds.All so the controller, Program.cs
        // registrations, and the reflection test all agree on one set of names.
        if (string.IsNullOrWhiteSpace(jobId)
            || !RentCollection.Application.Interfaces.RecurringJobIds.All.Contains(jobId))
            return BadRequest(new { success = false, message = $"Job '{jobId}' is not on the allow-list." });

        try
        {
            recurring.Trigger(jobId);
            return Json(new { success = true, message = $"Job '{jobId}' triggered. Watch /hangfire for status." });
        }
        catch (Exception ex)
        {
            // Don't echo internal exception text (connection strings, stack
            // details) to the browser — log it and return a generic message.
            _logger.LogError(ex, "Failed to trigger recurring job {JobId}", jobId);
            return BadRequest(new { success = false, message = $"Job '{jobId}' could not be triggered. Check the server log for details." });
        }
    }

    /// <summary>
    /// Confirm the period close. Snapshot the current close-state, write
    /// a ClosedPeriod row + AuditLog entry, return success/failure.
    /// Idempotent — re-closing the same period returns success without
    /// re-inserting.
    /// </summary>
    // SECURITY: ClosedPeriod is a GLOBAL table (no CompanyId column), so a
    // company Admin closing "their" month would lock the period for every
    // company. Restricted to SuperAdmin until ClosedPeriod gains a CompanyId
    // column + per-company close semantics (out of scope for this change).
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.SuperAdmin)]
    public async Task<IActionResult> ConfirmClose(string period, string? notes,
        [FromServices] RentCollection.Application.Interfaces.IAuditLogService audit)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(period ?? "", @"^\d{4}-\d{2}$"))
            return BadRequest(new { success = false, message = "Period must be in yyyy-MM format." });

        var existing = await _context.ClosedPeriods.FirstOrDefaultAsync(c => c.Period == period);
        if (existing != null)
            return Json(new { success = true, message = $"Period {period} was already closed on {existing.ClosedAt:yyyy-MM-dd HH:mm} UTC by {existing.ClosedBy}." });

        var state = await _mec.BuildCloseStateAsync(period);
        var snapshot = System.Text.Json.JsonSerializer.Serialize(state);

        var row = new ClosedPeriod
        {
            Period = period!,
            ClosedAt = DateTime.UtcNow,
            ClosedBy = User.Identity?.Name ?? "system",
            SnapshotJson = snapshot,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes!.Trim(),
        };
        _context.ClosedPeriods.Add(row);

        await audit.WriteAsync(
            entityType: "ClosedPeriod",
            entityId: 0, // populated after save
            action: "PeriodClosed",
            byUser: User.Identity?.Name ?? "system",
            payload: new { period, hasNotes = !string.IsNullOrWhiteSpace(notes) });

        await _context.SaveChangesAsync();
        return Json(new { success = true, message = $"Period {period} closed. Snapshot saved." });
    }

    // BuildCloseStateAsync was extracted to MonthEndCloseService (Sprint 1
    // reliability work) so the 9-step aggregator could be unit-tested. The
    // controller now delegates to `_mec` for every entry point.

    #endregion

    #region Reports landing — snapshot endpoint (Sprint 1/2)

    /// <summary>
    /// Lightweight payload for the /Reports landing page. One controller call
    /// returns the 4 KPI cards (with prior-period deltas), a 12-month revenue
    /// series for the trend chart, an invoice-status breakdown for the donut,
    /// and the top 5 properties by collected — everything the landing renders.
    ///
    /// Heavy detail reports stay in their own actions. This endpoint exists
    /// so the landing renders in one round trip instead of five.
    /// </summary>
    // Location = Client (not Any): the payload is company-scoped, so a shared
    // proxy cache keyed only on from/to would hand company A's figures to company B.
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Client, VaryByQueryKeys = new[] { "from", "to" })]
    public async Task<IActionResult> Snapshot(DateTime? from, DateTime? to)
    {
        var today = DateTime.Today;
        var startDate = from ?? new DateTime(today.Year, today.Month, 1);
        var endDate = to ?? today;
        var endExcl = EndExclusive(endDate);
        var rangeDays = Math.Max(1, (int)(endDate - startDate).TotalDays + 1);

        // Same-length prior period for delta chips: if you're looking at
        // 2026-05-01..2026-05-25 (25 days), prior is 2026-04-06..2026-04-30.
        var prevEnd = startDate.AddDays(-1);
        var prevEndExcl = EndExclusive(prevEnd);
        var prevStart = prevEnd.AddDays(-(rangeDays - 1));

        // Company scope (see ScopedCompanyId). Applied to every unfiltered set.
        var cid = ScopedCompanyId();
        var payments = _context.Payments.AsQueryable();
        var invoices = _context.Invoices.AsQueryable();
        var units = _context.Units.AsQueryable();
        if (cid != null)
        {
            payments = payments.Where(p => p.Tenant.CompanyId == cid);
            invoices = invoices.Where(i => i.Tenant.CompanyId == cid);
            units = units.Where(u => u.Property.CompanyId == cid);
        }

        // --- Current period KPIs ---
        // EF Core's DbContext is NOT thread-safe, so each query is awaited
        // serially. Earlier this method kicked them off in parallel and hit
        // "A second operation was started on this context instance".
        var collected = await payments
            .Where(p => p.PaymentDate >= startDate && p.PaymentDate < endExcl
                        && p.Status == PaymentStatus.Completed)
            .SumAsync(p => (decimal?)p.AmountPaid) ?? 0m;

        var prevCollected = await payments
            .Where(p => p.PaymentDate >= prevStart && p.PaymentDate < prevEndExcl
                        && p.Status == PaymentStatus.Completed)
            .SumAsync(p => (decimal?)p.AmountPaid) ?? 0m;

        // Cancelled invoices are not receivable — exclude them from outstanding.
        var outstanding = await invoices
            .Where(i => i.Status != "Paid" && i.Status != "Cancelled")
            .SumAsync(i => (decimal?)(i.TotalAmount - i.PaidAmount)) ?? 0m;

        var overdueCount = await invoices
            .CountAsync(i => i.Status != "Paid" && i.Status != "Cancelled" && i.DueDate < today);

        var prevOverdueCount = await invoices
            .CountAsync(i => i.Status != "Paid" && i.Status != "Cancelled" && i.DueDate < prevEnd);

        var totalUnits = await units.CountAsync();
        var occupiedUnits = await units.CountAsync(u => u.Status == "Occupied");
        var occupancy = totalUnits > 0 ? (decimal)occupiedUnits / totalUnits * 100m : 0m;

        // --- 12-month revenue trend + same months one year earlier (YoY overlay) ---
        var trendStart = new DateTime(today.Year, today.Month, 1).AddMonths(-11);
        var yoyStart = trendStart.AddYears(-1);
        var monthlyRevenueRaw = await payments
            .Where(p => p.PaymentDate >= yoyStart && p.PaymentDate < EndExclusive(today)
                        && p.Status == PaymentStatus.Completed)
            .GroupBy(p => new { p.PaymentDate.Year, p.PaymentDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(p => p.AmountPaid) })
            .ToListAsync();

        var trend = new List<object>();
        for (int i = 0; i < 12; i++)
        {
            var m = trendStart.AddMonths(i);
            var prev = m.AddYears(-1);
            var thisYear = monthlyRevenueRaw.FirstOrDefault(x => x.Year == m.Year && x.Month == m.Month);
            var lastYear = monthlyRevenueRaw.FirstOrDefault(x => x.Year == prev.Year && x.Month == prev.Month);
            trend.Add(new
            {
                label = m.ToString("MMM yy"),
                value = thisYear?.Total ?? 0m,
                previous = lastYear?.Total ?? 0m,
            });
        }

        // --- Invoice status donut ---
        var statusRaw = await invoices
            .GroupBy(i => i.Status)
            .Select(g => new { status = g.Key, count = g.Count() })
            .ToListAsync();

        // Overdue is a derived state — split Pending into Pending + Overdue.
        var pendingButOverdue = await invoices
            .CountAsync(i => i.Status == "Pending" && i.DueDate < today);
        var statusBreakdown = new List<object>();
        foreach (var s in statusRaw)
        {
            if (s.status == "Pending")
            {
                statusBreakdown.Add(new { status = "Pending", count = s.count - pendingButOverdue });
                statusBreakdown.Add(new { status = "Overdue", count = pendingButOverdue });
            }
            else
            {
                statusBreakdown.Add(new { status = s.status, count = s.count });
            }
        }

        // --- Top 5 properties by collected in range ---
        var topPropertiesRaw = await payments
            .Where(p => p.PaymentDate >= startDate && p.PaymentDate < endExcl
                        && p.Status == PaymentStatus.Completed
                        && p.PropertyId != 0)
            .GroupBy(p => p.PropertyId)
            .Select(g => new { PropertyId = g.Key, Total = g.Sum(x => x.AmountPaid) })
            .OrderByDescending(x => x.Total)
            .Take(5)
            .ToListAsync();
        var propertyIds = topPropertiesRaw.Select(t => t.PropertyId).ToList();
        var propertyNames = await _context.Properties
            .Where(p => propertyIds.Contains(p.PropertyId))
            .Select(p => new { p.PropertyId, p.PropertyName })
            .ToDictionaryAsync(p => p.PropertyId, p => p.PropertyName);
        var topProperties = topPropertiesRaw.Select(t => new
        {
            name = propertyNames.GetValueOrDefault(t.PropertyId) ?? "—",
            value = t.Total,
        }).ToList();

        decimal Delta(decimal cur, decimal prev) =>
            prev == 0 ? (cur == 0 ? 0 : 100) : Math.Round((cur - prev) / prev * 100m, 1);

        return Json(new
        {
            generatedAt = DateTime.UtcNow,
            from = startDate,
            to = endDate,
            kpis = new
            {
                collected = new { value = collected, delta = Delta(collected, prevCollected), goodWhenUp = true },
                outstanding = new { value = outstanding, delta = (decimal?)null, goodWhenUp = false },
                overdueCount = new { value = overdueCount, delta = Delta(overdueCount, prevOverdueCount), goodWhenUp = false },
                occupancy = new { value = occupancy, delta = (decimal?)null, goodWhenUp = true },
            },
            trend,
            statusBreakdown,
            topProperties,
        });
    }

    /// <summary>
    /// Executive Dashboard as a PDF — same data shape as the JSON snapshot,
    /// rendered via QuestPDF. Drives both the on-demand "Download PDF" button
    /// on the landing and the scheduled monthly email job (Sprint 4).
    ///
    /// `[ResponseCache]` keeps repeated requests from re-running the
    /// aggregations within a short window; the per-user vary headers make
    /// sure two admins don't share each other's cookie cache.
    /// </summary>
    // Location = Client so a shared cache can never serve one company's PDF to another.
    // TODO (company scope): IExecutiveDashboardComposer.BuildAsync has no companyId
    // parameter yet, so for a non-SuperAdmin this PDF still aggregates across all
    // companies. Pass ScopedCompanyId() once the composer accepts it.
    [HttpGet]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Client, VaryByQueryKeys = new[] { "from", "to" })]
    public async Task<IActionResult> ExecutiveDashboardPdf(DateTime? from, DateTime? to)
    {
        // Company-scoped for non-SuperAdmins (null = system-wide for SuperAdmin).
        var model = await _composer.BuildAsync(from, to, ScopedCompanyId());
        var bytes = _pdf.RenderExecutiveDashboard(model);
        var name = $"executive-dashboard-{model.FromDate:yyyy-MM-dd}-to-{model.ToDate:yyyy-MM-dd}.pdf";
        return File(bytes, "application/pdf", name);
    }

    #endregion

    #region Report 1: Executive Dashboard

    public async Task<IActionResult> ExecutiveDashboard(DateTime? fromDate, DateTime? toDate)
    {
        var startDate = fromDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var endDate = toDate ?? DateTime.Today;
        var endExcl = EndExclusive(endDate);

        // Company scope — Units/Invoices/Payments/MaintenanceRequests/Leases have
        // no EF company filter, so scope them through the filtered parent.
        var cid = ScopedCompanyId();
        var units = _context.Units.AsQueryable();
        var invoices = _context.Invoices.AsQueryable();
        var payments = _context.Payments.AsQueryable();
        var maintenance = _context.MaintenanceRequests.AsQueryable();
        var leases = _context.Leases.AsQueryable();
        if (cid != null)
        {
            units = units.Where(u => u.Property.CompanyId == cid);
            invoices = invoices.Where(i => i.Tenant.CompanyId == cid);
            payments = payments.Where(p => p.Tenant.CompanyId == cid);
            maintenance = maintenance.Where(m => m.Property.CompanyId == cid);
            leases = leases.Where(l => l.Tenant.CompanyId == cid);
        }

        // Total Properties
        var totalProperties = await _context.Properties.CountAsync();

        // Total Units
        var totalUnits = await units.CountAsync();

        // Occupied Units
        var occupiedUnits = await units.CountAsync(u => u.Status == "Occupied");

        // Vacant Units
        var vacantUnits = await units.CountAsync(u => u.Status == "Vacant");

        // Occupancy Rate
        var occupancyRate = totalUnits > 0 ? (decimal)occupiedUnits / totalUnits * 100 : 0;

        // Total Monthly Rent (Potential)
        var potentialMonthlyRent = await units.SumAsync(u => u.MonthlyRent);

        // Total Monthly Rent (Actual - from occupied units)
        var actualMonthlyRent = await units
            .Where(u => u.Status == "Occupied")
            .SumAsync(u => u.MonthlyRent);

        // Total Outstanding (Cancelled invoices are not receivable)
        var totalOutstanding = await invoices
            .Where(i => i.Status != "Paid" && i.Status != "Cancelled")
            .SumAsync(i => i.TotalAmount - i.PaidAmount);

        // YTD Revenue
        var yearStart = new DateTime(endDate.Year, 1, 1);
        var ytdRevenue = await payments
            .Where(p => p.PaymentDate >= yearStart && p.PaymentDate < endExcl && p.Status == PaymentStatus.Completed)
            .SumAsync(p => p.AmountPaid);

        // Maintenance Cost (Current Period)
        var maintenanceCost = await maintenance
            .Where(m => m.RequestDate >= startDate && m.RequestDate < endExcl && m.Status == "Completed")
            .SumAsync(m => m.ActualCost ?? 0);

        // Maintenance Count
        var maintenanceCount = await maintenance
            .Where(m => m.RequestDate >= startDate && m.RequestDate < endExcl && m.Status == "Completed")
            .CountAsync();

        // Active Tenants
        var activeTenants = await _context.Tenants.CountAsync(t => t.Status == "Active");

        // Active Leases
        var activeLeases = await leases.CountAsync(l => l.Status == "Active");

        // Average Rent Per Tenant
        var avgRentPerTenant = activeTenants > 0 ? actualMonthlyRent / activeTenants : 0;

        // Average Maintenance Cost
        var avgMaintenanceCost = maintenanceCount > 0 ? maintenanceCost / maintenanceCount : 0;

        // Expiring Leases (Next 30 Days)
        var next30Days = DateTime.Today.AddDays(30);
        var expiringLeases = await leases
            .CountAsync(l => l.Status == "Active" && l.LeaseEndDate <= next30Days && l.LeaseEndDate >= DateTime.Today);

        // Collection Rate
        var totalInvoiced = await invoices
            .Where(i => i.InvoiceDate >= startDate && i.InvoiceDate < endExcl)
            .SumAsync(i => i.TotalAmount);
        var totalPaid = await invoices
            .Where(i => i.InvoiceDate >= startDate && i.InvoiceDate < endExcl)
            .SumAsync(i => i.PaidAmount);
        var collectionRate = totalInvoiced > 0 ? (totalPaid / totalInvoiced * 100) : 0;

        // Previous Month Comparison
        var prevMonthStart = startDate.AddMonths(-1);
        var prevMonthEndExcl = EndExclusive(endDate.AddMonths(-1));
        var prevMonthRevenue = await payments
            .Where(p => p.PaymentDate >= prevMonthStart && p.PaymentDate < prevMonthEndExcl && p.Status == PaymentStatus.Completed)
            .SumAsync(p => p.AmountPaid);

        var revenueChange = prevMonthRevenue > 0
            ? ((actualMonthlyRent - prevMonthRevenue) / prevMonthRevenue * 100)
            : 0;

        // ── Owner-dashboard presentation (Oct 2026) ──
        // Open / overdue receivable counts for the KPI row. Overdue = receivable
        // whose due date has already passed, as of today.
        var openSet = invoices.Where(i => i.Status != "Paid" && i.Status != "Cancelled");
        var outstandingInvoices = await openSet.CountAsync();
        var overdueSet = openSet.Where(i => i.DueDate < DateTime.Today);
        var overdueAmount = await overdueSet.SumAsync(i => (decimal?)(i.TotalAmount - i.PaidAmount)) ?? 0m;
        var overdueInvoices = await overdueSet.CountAsync();

        // Property health: per-property occupancy plus the period's invoiced /
        // collected totals. Properties carry the EF company filter; the invoice
        // set is already company-scoped above (Invoice → Lease → Unit → Property).
        var propertyRows = await _context.Properties
            .OrderBy(p => p.PropertyName)
            .Select(p => new RentCollection.Web.Models.ExecutivePropertyRow
            {
                PropertyId = p.PropertyId,
                Name = p.PropertyName,
                TotalUnits = p.Units.Count(),
                OccupiedUnits = p.Units.Count(u => u.Status == "Occupied"),
            })
            .ToListAsync();
        var periodByProperty = await invoices
            .Where(i => i.InvoiceDate >= startDate && i.InvoiceDate < endExcl)
            .GroupBy(i => i.Lease.Unit.PropertyId)
            .Select(g => new
            {
                PropertyId = g.Key,
                Invoiced = g.Sum(i => i.TotalAmount),
                Collected = g.Sum(i => i.PaidAmount),
            })
            .ToListAsync();
        foreach (var row in propertyRows)
        {
            var hit = periodByProperty.FirstOrDefault(x => x.PropertyId == row.PropertyId);
            if (hit == null) continue;
            row.Invoiced = hit.Invoiced;
            row.Collected = hit.Collected;
        }
        // Weakest collection rate first; properties with nothing invoiced sink to the end.
        propertyRows = propertyRows
            .OrderBy(r => r.CollectionRate ?? decimal.MaxValue)
            .ThenBy(r => r.Name)
            .ToList();

        // Collections trend: the six calendar months ending with the report's end
        // month. Expected = non-cancelled invoices by invoice month; collected =
        // completed payments by payment month. Heights are scaled to one shared max.
        var trendMonthStart = new DateTime(endDate.Year, endDate.Month, 1);
        var trendStart = trendMonthStart.AddMonths(-5);
        var trendEndExcl = trendMonthStart.AddMonths(1);
        var invoicedByMonth = await invoices
            .Where(i => i.InvoiceDate >= trendStart && i.InvoiceDate < trendEndExcl && i.Status != "Cancelled")
            .GroupBy(i => new { i.InvoiceDate.Year, i.InvoiceDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(i => i.TotalAmount) })
            .ToListAsync();
        var collectedByMonth = await payments
            .Where(p => p.PaymentDate >= trendStart && p.PaymentDate < trendEndExcl && p.Status == PaymentStatus.Completed)
            .GroupBy(p => new { p.PaymentDate.Year, p.PaymentDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(p => p.AmountPaid) })
            .ToListAsync();
        var trend = new List<RentCollection.Web.Models.ExecutiveTrendPoint>(6);
        for (var m = trendStart; m < trendEndExcl; m = m.AddMonths(1))
        {
            trend.Add(new RentCollection.Web.Models.ExecutiveTrendPoint
            {
                Label = m.ToString("MMM"),
                Expected = invoicedByMonth.FirstOrDefault(x => x.Year == m.Year && x.Month == m.Month)?.Total ?? 0m,
                Collected = collectedByMonth.FirstOrDefault(x => x.Year == m.Year && x.Month == m.Month)?.Total ?? 0m,
                IsPartial = m.Year == DateTime.Today.Year && m.Month == DateTime.Today.Month,
            });
        }
        var trendMax = trend.Max(t => Math.Max(t.Expected, t.Collected));
        foreach (var t in trend)
        {
            t.ExpectedPx = RentCollection.Web.Models.ExecutiveTrendPoint.ToPx(t.Expected, trendMax);
            t.CollectedPx = RentCollection.Web.Models.ExecutiveTrendPoint.ToPx(t.Collected, trendMax);
        }

        ViewBag.FromDate = startDate;
        ViewBag.ToDate = endDate;
        ViewBag.TotalProperties = totalProperties;
        ViewBag.TotalUnits = totalUnits;
        ViewBag.OccupiedUnits = occupiedUnits;
        ViewBag.VacantUnits = vacantUnits;
        ViewBag.OccupancyRate = occupancyRate;
        ViewBag.PotentialMonthlyRent = potentialMonthlyRent;
        ViewBag.ActualMonthlyRent = actualMonthlyRent;
        ViewBag.TotalOutstanding = totalOutstanding;
        ViewBag.YTDRevenue = ytdRevenue;
        ViewBag.YTDMaintenanceCosts = maintenanceCost;
        ViewBag.MaintenanceCount = maintenanceCount;
        ViewBag.ActiveTenants = activeTenants;
        ViewBag.ActiveLeases = activeLeases;
        ViewBag.ExpiringLeases = expiringLeases;
        ViewBag.CollectionRate = collectionRate;
        ViewBag.PrevMonthRevenue = prevMonthRevenue;
        ViewBag.RevenueChange = revenueChange;
        ViewBag.NetRevenue = actualMonthlyRent - maintenanceCost;
        ViewBag.AvgRentPerTenant = avgRentPerTenant;
        ViewBag.AvgMaintenanceCost = avgMaintenanceCost;
        // Owner-dashboard presentation extras.
        ViewBag.PeriodInvoiced = totalInvoiced;      // denominator of CollectionRate
        ViewBag.PeriodCollected = totalPaid;         // numerator of CollectionRate
        ViewBag.OutstandingInvoices = outstandingInvoices;
        ViewBag.OverdueAmount = overdueAmount;
        ViewBag.OverdueInvoices = overdueInvoices;
        ViewBag.PropertyRows = propertyRows;
        ViewBag.Trend = trend;

        return View();
    }

    public async Task<IActionResult> ExecutiveDashboardExcel(DateTime? fromDate, DateTime? toDate)
    {
        var startDate = fromDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var endDate = toDate ?? DateTime.Today;
        var endExcl = EndExclusive(endDate);

        // Company scope (same approach as ExecutiveDashboard).
        var cid = ScopedCompanyId();
        var units = _context.Units.AsQueryable();
        var invoices = _context.Invoices.AsQueryable();
        var payments = _context.Payments.AsQueryable();
        var maintenance = _context.MaintenanceRequests.AsQueryable();
        if (cid != null)
        {
            units = units.Where(u => u.Property.CompanyId == cid);
            invoices = invoices.Where(i => i.Tenant.CompanyId == cid);
            payments = payments.Where(p => p.Tenant.CompanyId == cid);
            maintenance = maintenance.Where(m => m.Property.CompanyId == cid);
        }

        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Executive Dashboard");

        // Title
        worksheet.Cells["A1"].Value = "EXECUTIVE DASHBOARD REPORT";
        worksheet.Cells["A1:E1"].Merge = true;
        worksheet.Cells["A1"].Style.Font.Size = 16;
        worksheet.Cells["A1"].Style.Font.Bold = true;
        worksheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        worksheet.Cells["A1"].Style.Fill.PatternType = ExcelFillStyle.Solid;
        worksheet.Cells["A1"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(59, 130, 246));
        worksheet.Cells["A1"].Style.Font.Color.SetColor(Color.White);

        worksheet.Cells["A2"].Value = $"Period: {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}";
        worksheet.Cells["A2:E2"].Merge = true;
        worksheet.Cells["A2"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

        int row = 4;

        // Portfolio Overview
        worksheet.Cells[row, 1].Value = "PORTFOLIO OVERVIEW";
        worksheet.Cells[row, 1].Style.Font.Bold = true;
        worksheet.Cells[row, 1].Style.Font.Size = 14;
        row += 2;

        worksheet.Cells[row, 1].Value = "Metric";
        worksheet.Cells[row, 2].Value = "Value";
        worksheet.Cells[row, 1, row, 2].Style.Font.Bold = true;
        worksheet.Cells[row, 1, row, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
        worksheet.Cells[row, 1, row, 2].Style.Fill.BackgroundColor.SetColor(Color.LightGray);
        row++;

        var totalProperties = await _context.Properties.CountAsync();
        var totalUnits = await units.CountAsync();
        var occupiedUnits = await units.CountAsync(u => u.Status == "Occupied");
        var vacantUnits = await units.CountAsync(u => u.Status == "Vacant");
        var occupancyRate = totalUnits > 0 ? (decimal)occupiedUnits / totalUnits * 100 : 0;

        worksheet.Cells[row, 1].Value = "Total Properties";
        worksheet.Cells[row++, 2].Value = totalProperties;

        worksheet.Cells[row, 1].Value = "Total Units";
        worksheet.Cells[row++, 2].Value = totalUnits;

        worksheet.Cells[row, 1].Value = "Occupied Units";
        worksheet.Cells[row++, 2].Value = occupiedUnits;

        worksheet.Cells[row, 1].Value = "Vacant Units";
        worksheet.Cells[row++, 2].Value = vacantUnits;

        worksheet.Cells[row, 1].Value = "Occupancy Rate";
        worksheet.Cells[row, 2].Value = $"{occupancyRate:N2}%";
        row += 2;

        // Financial Metrics
        worksheet.Cells[row, 1].Value = "FINANCIAL METRICS";
        worksheet.Cells[row, 1].Style.Font.Bold = true;
        worksheet.Cells[row, 1].Style.Font.Size = 14;
        row += 2;

        worksheet.Cells[row, 1].Value = "Metric";
        worksheet.Cells[row, 2].Value = "Amount (KWD)";
        worksheet.Cells[row, 1, row, 2].Style.Font.Bold = true;
        worksheet.Cells[row, 1, row, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
        worksheet.Cells[row, 1, row, 2].Style.Fill.BackgroundColor.SetColor(Color.LightGray);
        row++;

        var potentialRent = await units.SumAsync(u => u.MonthlyRent);
        var actualRent = await units.Where(u => u.Status == "Occupied").SumAsync(u => u.MonthlyRent);
        var totalOutstanding = await invoices.Where(i => i.Status != "Paid" && i.Status != "Cancelled").SumAsync(i => i.TotalAmount - i.PaidAmount);
        var yearStart = new DateTime(endDate.Year, 1, 1);
        var ytdRevenue = await payments.Where(p => p.PaymentDate >= yearStart && p.PaymentDate < endExcl && p.Status == PaymentStatus.Completed).SumAsync(p => p.AmountPaid);
        var maintenanceCost = await maintenance.Where(m => m.RequestDate >= startDate && m.RequestDate < endExcl && m.Status == "Completed").SumAsync(m => m.ActualCost ?? 0);

        worksheet.Cells[row, 1].Value = "Potential Monthly Rent";
        worksheet.Cells[row, 2].Value = potentialRent;
        worksheet.Cells[row, 2].Style.Numberformat.Format = "#,##0.000";
        row++;

        worksheet.Cells[row, 1].Value = "Actual Monthly Rent";
        worksheet.Cells[row, 2].Value = actualRent;
        worksheet.Cells[row, 2].Style.Numberformat.Format = "#,##0.000";
        row++;

        worksheet.Cells[row, 1].Value = "Total Outstanding";
        worksheet.Cells[row, 2].Value = totalOutstanding;
        worksheet.Cells[row, 2].Style.Numberformat.Format = "#,##0.000";
        row++;

        worksheet.Cells[row, 1].Value = "YTD Revenue";
        worksheet.Cells[row, 2].Value = ytdRevenue;
        worksheet.Cells[row, 2].Style.Numberformat.Format = "#,##0.000";
        row++;

        worksheet.Cells[row, 1].Value = "Maintenance Cost (Period)";
        worksheet.Cells[row, 2].Value = maintenanceCost;
        worksheet.Cells[row, 2].Style.Numberformat.Format = "#,##0.000";
        row++;

        worksheet.Cells[row, 1].Value = "Net Revenue (Period)";
        worksheet.Cells[row, 2].Value = actualRent - maintenanceCost;
        worksheet.Cells[row, 2].Style.Numberformat.Format = "#,##0.000";
        worksheet.Cells[row, 2].Style.Font.Bold = true;

        worksheet.Cells.AutoFitColumns();

        var fileName = $"Executive_Dashboard_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var fileBytes = package.GetAsByteArray();
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    #endregion

    #region Report 2: Property Performance

    public async Task<IActionResult> PropertyPerformance(DateTime? fromDate, DateTime? toDate)
    {
        var startDate = fromDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var endDate = toDate ?? DateTime.Today;
        var endExcl = EndExclusive(endDate);

        // Properties carry the EF company filter, and every sub-query below is
        // keyed on a scoped PropertyId, so this report is company-safe as-is.
        var properties = await _context.Properties
            .Include(p => p.Units)
                .ThenInclude(u => u.Leases)
            .ToListAsync();

        var model = new List<PropertyPerformanceData>();

        foreach (var property in properties)
        {
            var totalUnits = property.Units.Count;
            var occupiedUnits = property.Units.Count(u => u.Status == "Occupied");
            var vacantUnits = property.Units.Count(u => u.Status == "Vacant");
            var occupancyRate = totalUnits > 0 ? (decimal)occupiedUnits / totalUnits * 100 : 0;

            var potentialRent = property.Units.Sum(u => u.MonthlyRent);
            var actualRent = property.Units.Where(u => u.Status == "Occupied").Sum(u => u.MonthlyRent);
            // NOTE: this is rent-weighted occupancy (occupied rent / potential rent),
            // not a true collection rate (paid / invoiced). The view model property
            // name is kept for view compatibility.
            var collectionRate = potentialRent > 0 ? (actualRent / potentialRent * 100) : 0;

            var outstandingAmount = await _context.Invoices
                .Include(i => i.Lease)
                    .ThenInclude(l => l.Unit)
                .Where(i => i.Lease.Unit.PropertyId == property.PropertyId
                            && i.Status != "Paid" && i.Status != "Cancelled")
                .SumAsync(i => i.TotalAmount - i.PaidAmount);

            var maintenanceCost = await _context.MaintenanceRequests
                .Where(m => m.PropertyId == property.PropertyId &&
                           m.RequestDate >= startDate &&
                           m.RequestDate < endExcl &&
                           m.Status == "Completed")
                .SumAsync(m => m.ActualCost ?? 0);

            var maintenanceCount = await _context.MaintenanceRequests
                .Where(m => m.PropertyId == property.PropertyId &&
                           m.RequestDate >= startDate &&
                           m.RequestDate < endExcl)
                .CountAsync();

            var totalSquareFeet = property.Units.Sum(u => (decimal?)u.SquareFeet) ?? 0;
            var revenuePerSqFt = totalSquareFeet > 0 ? actualRent / totalSquareFeet : 0;

            model.Add(new PropertyPerformanceData
            {
                PropertyName = property.PropertyName,
                PACINumber = property.PACINumber ?? "N/A",
                Location = $"{property.Governorate}, {property.Area}",
                TotalUnits = totalUnits,
                OccupiedUnits = occupiedUnits,
                VacantUnits = vacantUnits,
                OccupancyRate = occupancyRate,
                PotentialRent = potentialRent,
                ActualRent = actualRent,
                CollectionRate = collectionRate,
                OutstandingAmount = outstandingAmount,
                MaintenanceCost = maintenanceCost,
                MaintenanceCount = maintenanceCount,
                NetRevenue = actualRent - maintenanceCost,
                RevenuePerSqFt = revenuePerSqFt
            });
        }

        ViewBag.FromDate = startDate;
        ViewBag.ToDate = endDate;

        return View(model.OrderByDescending(p => p.NetRevenue).ToList());
    }

    public async Task<IActionResult> PropertyPerformanceExcel(DateTime? fromDate, DateTime? toDate)
    {
        var startDate = fromDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var endDate = toDate ?? DateTime.Today;
        var endExcl = EndExclusive(endDate);

        var properties = await _context.Properties
            .Include(p => p.Units)
            .ToListAsync();

        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Property Performance");

        // Headers
        var headers = new[] {
            "Property Name", "PACI", "Location", "Total Units", "Occupied", "Vacant",
            "Occupancy %", "Potential Rent (KWD)", "Actual Rent (KWD)", "Rent Occupancy %",
            "Outstanding (KWD)", "Maintenance Cost (KWD)", "Maintenance Count",
            "Net Revenue (KWD)", "Revenue/SqFt"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            worksheet.Cells[1, i + 1].Value = headers[i];
        }

        // Header styling
        worksheet.Cells[1, 1, 1, headers.Length].Style.Font.Bold = true;
        worksheet.Cells[1, 1, 1, headers.Length].Style.Fill.PatternType = ExcelFillStyle.Solid;
        worksheet.Cells[1, 1, 1, headers.Length].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(59, 130, 246));
        worksheet.Cells[1, 1, 1, headers.Length].Style.Font.Color.SetColor(Color.White);

        int row = 2;
        foreach (var property in properties)
        {
            var totalUnits = property.Units.Count;
            var occupiedUnits = property.Units.Count(u => u.Status == "Occupied");
            var vacantUnits = property.Units.Count(u => u.Status == "Vacant");
            var occupancyRate = totalUnits > 0 ? (decimal)occupiedUnits / totalUnits * 100 : 0;

            var potentialRent = property.Units.Sum(u => u.MonthlyRent);
            var actualRent = property.Units.Where(u => u.Status == "Occupied").Sum(u => u.MonthlyRent);
            var collectionRate = potentialRent > 0 ? (actualRent / potentialRent * 100) : 0;

            var outstandingAmount = await _context.Invoices
                .Include(i => i.Lease).ThenInclude(l => l.Unit)
                .Where(i => i.Lease.Unit.PropertyId == property.PropertyId
                            && i.Status != "Paid" && i.Status != "Cancelled")
                .SumAsync(i => i.TotalAmount - i.PaidAmount);

            var maintenanceCost = await _context.MaintenanceRequests
                .Where(m => m.PropertyId == property.PropertyId && m.RequestDate >= startDate && m.RequestDate < endExcl && m.Status == "Completed")
                .SumAsync(m => m.ActualCost ?? 0);

            var maintenanceCount = await _context.MaintenanceRequests
                .Where(m => m.PropertyId == property.PropertyId && m.RequestDate >= startDate && m.RequestDate < endExcl)
                .CountAsync();

            var totalSquareFeet = property.Units.Sum(u => (decimal?)u.SquareFeet) ?? 0;
            var revenuePerSqFt = totalSquareFeet > 0 ? actualRent / totalSquareFeet : 0;

            worksheet.Cells[row, 1].Value = property.PropertyName;
            worksheet.Cells[row, 2].Value = property.PACINumber ?? "N/A";
            worksheet.Cells[row, 3].Value = $"{property.Governorate}, {property.Area}";
            worksheet.Cells[row, 4].Value = totalUnits;
            worksheet.Cells[row, 5].Value = occupiedUnits;
            worksheet.Cells[row, 6].Value = vacantUnits;
            worksheet.Cells[row, 7].Value = occupancyRate;
            worksheet.Cells[row, 7].Style.Numberformat.Format = "0.00\"%\"";
            worksheet.Cells[row, 8].Value = potentialRent;
            worksheet.Cells[row, 8].Style.Numberformat.Format = "#,##0.000";
            worksheet.Cells[row, 9].Value = actualRent;
            worksheet.Cells[row, 9].Style.Numberformat.Format = "#,##0.000";
            worksheet.Cells[row, 10].Value = collectionRate;
            worksheet.Cells[row, 10].Style.Numberformat.Format = "0.00\"%\"";
            worksheet.Cells[row, 11].Value = outstandingAmount;
            worksheet.Cells[row, 11].Style.Numberformat.Format = "#,##0.000";
            worksheet.Cells[row, 12].Value = maintenanceCost;
            worksheet.Cells[row, 12].Style.Numberformat.Format = "#,##0.000";
            worksheet.Cells[row, 13].Value = maintenanceCount;
            worksheet.Cells[row, 14].Value = actualRent - maintenanceCost;
            worksheet.Cells[row, 14].Style.Numberformat.Format = "#,##0.000";
            worksheet.Cells[row, 15].Value = revenuePerSqFt;
            worksheet.Cells[row, 15].Style.Numberformat.Format = "#,##0.000";

            row++;
        }

        worksheet.Cells.AutoFitColumns();

        var fileName = $"Property_Performance_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var fileBytes = package.GetAsByteArray();
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    #endregion

    #region Report 3: Tenant Payment Analysis

    public async Task<IActionResult> TenantPaymentAnalysis(DateTime? fromDate, DateTime? toDate)
    {
        var startDate = fromDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var endDate = toDate ?? DateTime.Today;

        // Tenants carry the EF company filter; every Invoice/Payment query below
        // is keyed on a scoped TenantId, so this report is company-safe as-is.
        var tenants = await _context.Tenants
            .Include(t => t.Leases)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .Where(t => t.Status == "Active")
            .ToListAsync();

        var model = new List<TenantPaymentData>();

        foreach (var tenant in tenants)
        {
            foreach (var lease in tenant.Leases.Where(l => l.Status == "Active"))
            {
                var invoices = await _context.Invoices
                    .Where(i => i.TenantId == tenant.TenantId && i.LeaseId == lease.LeaseId)
                    .ToListAsync();

                var totalInvoiced = invoices.Sum(i => i.TotalAmount);
                var totalPaid = invoices.Sum(i => i.PaidAmount);
                var outstanding = totalInvoiced - totalPaid;

                var overdueInvoices = invoices.Where(i => i.Status != "Paid" && i.Status != "Cancelled" && i.DueDate < DateTime.Today).ToList();
                var daysOverdue = overdueInvoices.Any() ? (int)(DateTime.Today - overdueInvoices.Min(i => i.DueDate)).TotalDays : 0;

                var lateFees = overdueInvoices.Count * (lease.LateFeeAmount ?? 0);

                // Get all payments for this tenant
                var payments = await _context.Payments
                    .Where(p => p.TenantId == tenant.TenantId && p.LeaseId == lease.LeaseId && p.Status == PaymentStatus.Completed)
                    .OrderByDescending(p => p.PaymentDate)
                    .ToListAsync();

                var lastPayment = payments.FirstOrDefault();
                var totalPayments = payments.Count;

                // Calculate average days to pay
                // Simplified calculation: average time from invoice due date to payment
                var avgDaysToPay = 0;
                if (totalPayments > 0 && payments.Any())
                {
                    var paidInvoices = invoices.Where(i => i.PaidAmount > 0).ToList();
                    if (paidInvoices.Any())
                    {
                        // Average days between invoice date and payment date
                        var totalDays = 0;
                        int countedPayments = 0;
                        foreach (var payment in payments)
                        {
                            // Find invoices around this payment date
                            var relatedInvoice = paidInvoices
                                .Where(i => i.InvoiceDate <= payment.PaymentDate)
                                .OrderByDescending(i => i.InvoiceDate)
                                .FirstOrDefault();

                            if (relatedInvoice != null)
                            {
                                totalDays += (int)(payment.PaymentDate - relatedInvoice.InvoiceDate).TotalDays;
                                countedPayments++;
                            }
                        }
                        avgDaysToPay = countedPayments > 0 ? totalDays / countedPayments : 0;
                    }
                }

                // Payment Rate
                var paymentRate = totalInvoiced > 0 ? (totalPaid / totalInvoiced) * 100 : 0;

                // Payment Score — count payments that landed after the linked
                // invoice's due date (with a 5-day grace). Previously this
                // compared a payment to its own CreatedDate, which is the
                // same wall-clock instant the row was inserted — always near
                // zero, so the score was meaningless.
                var latePaymentCount = await _context.Payments
                    .Where(p => p.TenantId == tenant.TenantId
                        && p.PaymentDate > DateTime.Today.AddMonths(-12)
                        && p.InvoiceId.HasValue
                        && p.Invoice != null
                        && p.PaymentDate > p.Invoice.DueDate.AddDays(5))
                    .CountAsync();

                string paymentScore = latePaymentCount == 0 ? "Excellent" :
                                     latePaymentCount <= 2 ? "Good" :
                                     latePaymentCount <= 5 ? "Fair" : "Poor";

                string status = outstanding == 0 ? "Current" :
                               daysOverdue > 0 ? "Overdue" :
                               outstanding > 0 ? "Late" : "Current";

                // Count active leases for this tenant
                var activeLeases = tenant.Leases.Count(l => l.Status == "Active");

                model.Add(new TenantPaymentData
                {
                    TenantName = $"{tenant.FirstName} {tenant.LastName}",
                    PACINumber = tenant.PACINumber ?? "N/A",
                    Email = tenant.Email ?? "N/A",
                    Phone = tenant.Phone,
                    PropertyName = lease.Unit.Property.PropertyName,
                    UnitNumber = lease.Unit.UnitNumber,
                    ActiveLeases = activeLeases,
                    MonthlyRent = lease.MonthlyRent,
                    TotalBilled = totalInvoiced,
                    TotalInvoiced = totalInvoiced,
                    TotalPaid = totalPaid,
                    OutstandingAmount = outstanding,
                    Outstanding = outstanding,
                    PaymentRate = paymentRate,
                    DaysOverdue = daysOverdue,
                    LateFees = lateFees,
                    AvgDaysToPay = avgDaysToPay,
                    TotalPayments = totalPayments,
                    LastPaymentDate = lastPayment?.PaymentDate,
                    LastPaymentAmount = lastPayment?.AmountPaid ?? 0,
                    PaymentScore = paymentScore,
                    Status = status
                });
            }
        }

        ViewBag.FromDate = startDate;
        ViewBag.ToDate = endDate;

        return View(model.OrderByDescending(t => t.Outstanding).ToList());
    }

    public async Task<IActionResult> TenantPaymentAnalysisExcel(DateTime? fromDate, DateTime? toDate)
    {
        var startDate = fromDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var endDate = toDate ?? DateTime.Today;

        var tenants = await _context.Tenants
            .Include(t => t.Leases)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .Where(t => t.Status == "Active")
            .ToListAsync();

        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Tenant Payment Analysis");

        // Headers
        var headers = new[] {
            "Tenant Name", "PACI", "Phone", "Property", "Unit", "Monthly Rent (KWD)",
            "Total Invoiced (KWD)", "Total Paid (KWD)", "Outstanding (KWD)", "Days Overdue",
            "Late Fees (KWD)", "Last Payment Date", "Last Payment Amount (KWD)", "Payment Score", "Status"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            worksheet.Cells[1, i + 1].Value = headers[i];
        }

        worksheet.Cells[1, 1, 1, headers.Length].Style.Font.Bold = true;
        worksheet.Cells[1, 1, 1, headers.Length].Style.Fill.PatternType = ExcelFillStyle.Solid;
        worksheet.Cells[1, 1, 1, headers.Length].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(59, 130, 246));
        worksheet.Cells[1, 1, 1, headers.Length].Style.Font.Color.SetColor(Color.White);

        int row = 2;
        foreach (var tenant in tenants)
        {
            foreach (var lease in tenant.Leases.Where(l => l.Status == "Active"))
            {
                var invoices = await _context.Invoices
                    .Where(i => i.TenantId == tenant.TenantId && i.LeaseId == lease.LeaseId)
                    .ToListAsync();

                var totalInvoiced = invoices.Sum(i => i.TotalAmount);
                var totalPaid = invoices.Sum(i => i.PaidAmount);
                var outstanding = totalInvoiced - totalPaid;

                var overdueInvoices = invoices.Where(i => i.Status != "Paid" && i.Status != "Cancelled" && i.DueDate < DateTime.Today).ToList();
                var daysOverdue = overdueInvoices.Any() ? (int)(DateTime.Today - overdueInvoices.Min(i => i.DueDate)).TotalDays : 0;

                var lateFees = overdueInvoices.Count * (lease.LateFeeAmount ?? 0);

                var lastPayment = await _context.Payments
                    .Where(p => p.TenantId == tenant.TenantId && p.LeaseId == lease.LeaseId && p.Status == PaymentStatus.Completed)
                    .OrderByDescending(p => p.PaymentDate)
                    .FirstOrDefaultAsync();

                // Late = paid more than 5 days after the INVOICE due date (match
                // the HTML report; the old CreatedDate comparison was ~always 0).
                var latePaymentCount = await _context.Payments
                    .Where(p => p.TenantId == tenant.TenantId
                        && p.PaymentDate > DateTime.Today.AddMonths(-12)
                        && p.InvoiceId.HasValue
                        && p.Invoice != null
                        && p.PaymentDate > p.Invoice.DueDate.AddDays(5))
                    .CountAsync();

                string paymentScore = latePaymentCount == 0 ? "Excellent" :
                                     latePaymentCount <= 2 ? "Good" :
                                     latePaymentCount <= 5 ? "Fair" : "Poor";

                string status = outstanding == 0 ? "Current" :
                               daysOverdue > 0 ? "Overdue" :
                               outstanding > 0 ? "Late" : "Current";

                worksheet.Cells[row, 1].Value = $"{tenant.FirstName} {tenant.LastName}";
                worksheet.Cells[row, 2].Value = tenant.PACINumber ?? "N/A";
                worksheet.Cells[row, 3].Value = tenant.Phone;
                worksheet.Cells[row, 4].Value = lease.Unit.Property.PropertyName;
                worksheet.Cells[row, 5].Value = lease.Unit.UnitNumber;
                worksheet.Cells[row, 6].Value = lease.MonthlyRent;
                worksheet.Cells[row, 6].Style.Numberformat.Format = "#,##0.000";
                worksheet.Cells[row, 7].Value = totalInvoiced;
                worksheet.Cells[row, 7].Style.Numberformat.Format = "#,##0.000";
                worksheet.Cells[row, 8].Value = totalPaid;
                worksheet.Cells[row, 8].Style.Numberformat.Format = "#,##0.000";
                worksheet.Cells[row, 9].Value = outstanding;
                worksheet.Cells[row, 9].Style.Numberformat.Format = "#,##0.000";
                worksheet.Cells[row, 10].Value = daysOverdue;
                worksheet.Cells[row, 11].Value = lateFees;
                worksheet.Cells[row, 11].Style.Numberformat.Format = "#,##0.000";
                worksheet.Cells[row, 12].Value = lastPayment?.PaymentDate.ToString("yyyy-MM-dd") ?? "N/A";
                worksheet.Cells[row, 13].Value = lastPayment?.AmountPaid ?? 0;
                worksheet.Cells[row, 13].Style.Numberformat.Format = "#,##0.000";
                worksheet.Cells[row, 14].Value = paymentScore;
                worksheet.Cells[row, 15].Value = status;

                row++;
            }
        }

        worksheet.Cells.AutoFitColumns();

        var fileName = $"Tenant_Payment_Analysis_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var fileBytes = package.GetAsByteArray();
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    #endregion

    #region Report 4: Invoice Status

    public async Task<IActionResult> InvoiceStatus(DateTime? fromDate, DateTime? toDate)
    {
        var startDate = fromDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var endDate = toDate ?? DateTime.Today;
        var endExcl = EndExclusive(endDate);

        var cid = ScopedCompanyId();
        var invoiceQuery = _context.Invoices
            .Include(i => i.Tenant)
            .Include(i => i.Lease)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .Where(i => i.InvoiceDate >= startDate && i.InvoiceDate < endExcl);
        if (cid != null) invoiceQuery = invoiceQuery.Where(i => i.Tenant.CompanyId == cid);

        var invoices = await invoiceQuery
            .OrderByDescending(i => i.InvoiceDate)
            .ToListAsync();

        // Summary
        var totalInvoices = invoices.Count;
        var totalAmount = invoices.Sum(i => i.TotalAmount);
        var totalPaid = invoices.Sum(i => i.PaidAmount);
        var totalOutstanding = totalAmount - totalPaid;
        var collectionRate = totalAmount > 0 ? (totalPaid / totalAmount * 100) : 0;

        // By Status
        var paidInvoices = invoices.Where(i => i.Status == "Paid").ToList();
        var partialInvoices = invoices.Where(i => i.Status == "Partial" || (i.PaidAmount > 0 && i.PaidAmount < i.TotalAmount)).ToList();
        var unpaidInvoices = invoices.Where(i => i.Status != "Paid" && i.PaidAmount == 0).ToList();

        // Aging Analysis - for unpaid/partial amounts (Cancelled invoices are not receivable)
        var receivable = invoices.Where(i => i.Status != "Paid" && i.Status != "Cancelled").ToList();
        var aging0to30 = receivable.Where(i => (DateTime.Today - i.DueDate).TotalDays >= 0 && (DateTime.Today - i.DueDate).TotalDays <= 30).ToList();
        var aging31to60 = receivable.Where(i => (DateTime.Today - i.DueDate).TotalDays > 30 && (DateTime.Today - i.DueDate).TotalDays <= 60).ToList();
        var aging61to90 = receivable.Where(i => (DateTime.Today - i.DueDate).TotalDays > 60 && (DateTime.Today - i.DueDate).TotalDays <= 90).ToList();
        var agingOver90 = receivable.Where(i => (DateTime.Today - i.DueDate).TotalDays > 90).ToList();

        ViewBag.FromDate = startDate;
        ViewBag.ToDate = endDate;
        ViewBag.TotalInvoices = totalInvoices;
        ViewBag.TotalAmount = totalAmount;
        ViewBag.TotalPaid = totalPaid;
        ViewBag.TotalOutstanding = totalOutstanding;
        ViewBag.CollectionRate = collectionRate;

        // By Status - matching view expectations
        ViewBag.PaidInvoices = paidInvoices.Count;
        ViewBag.TotalPaidAmount = paidInvoices.Sum(i => i.PaidAmount);
        ViewBag.PartialInvoices = partialInvoices.Count;
        ViewBag.TotalPartialAmount = partialInvoices.Sum(i => i.TotalAmount - i.PaidAmount);
        ViewBag.UnpaidInvoices = unpaidInvoices.Count;
        ViewBag.TotalUnpaidAmount = unpaidInvoices.Sum(i => i.TotalAmount);

        // Aging Analysis - matching view expectations
        ViewBag.Aging0to30Count = aging0to30.Count;
        ViewBag.Aging0to30Amount = aging0to30.Any() ? aging0to30.Sum(i => i.TotalAmount - i.PaidAmount) : 0m;
        ViewBag.Aging31to60Count = aging31to60.Count;
        ViewBag.Aging31to60Amount = aging31to60.Any() ? aging31to60.Sum(i => i.TotalAmount - i.PaidAmount) : 0m;
        ViewBag.Aging61to90Count = aging61to90.Count;
        ViewBag.Aging61to90Amount = aging61to90.Any() ? aging61to90.Sum(i => i.TotalAmount - i.PaidAmount) : 0m;
        ViewBag.AgingOver90Count = agingOver90.Count;
        ViewBag.AgingOver90Amount = agingOver90.Any() ? agingOver90.Sum(i => i.TotalAmount - i.PaidAmount) : 0m;

        // Prepare data for the view
        var model = invoices.Select(inv => {
            var daysOverdue = inv.DueDate < DateTime.Today ? (int)(DateTime.Today - inv.DueDate).TotalDays : 0;
            var outstanding = inv.TotalAmount - inv.PaidAmount;

            string agingBucket = "Current";
            if (outstanding > 0)
            {
                if (daysOverdue <= 30) agingBucket = "0-30 days";
                else if (daysOverdue <= 60) agingBucket = "31-60 days";
                else if (daysOverdue <= 90) agingBucket = "61-90 days";
                else agingBucket = "Over 90 days";
            }

            return new InvoiceStatusData
            {
                InvoiceNumber = $"INV-{inv.InvoiceId:D6}",
                TenantName = inv.Tenant != null ? $"{inv.Tenant.FirstName} {inv.Tenant.LastName}" : "N/A",
                PropertyName = inv.Lease?.Unit?.Property?.PropertyName ?? "N/A",
                UnitNumber = inv.Lease?.Unit?.UnitNumber ?? "N/A",
                InvoiceDate = inv.InvoiceDate,
                DueDate = inv.DueDate,
                TotalAmount = inv.TotalAmount,
                PaidAmount = inv.PaidAmount,
                OutstandingAmount = outstanding,
                Status = inv.Status,
                DaysOverdue = daysOverdue,
                AgingBucket = agingBucket
            };
        }).ToList();

        return View(model);
    }

    public async Task<IActionResult> InvoiceStatusExcel(DateTime? fromDate, DateTime? toDate)
    {
        var startDate = fromDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var endDate = toDate ?? DateTime.Today;
        var endExcl = EndExclusive(endDate);

        var cid = ScopedCompanyId();
        var invoiceQuery = _context.Invoices
            .Include(i => i.Tenant)
            .Include(i => i.Lease)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .Where(i => i.InvoiceDate >= startDate && i.InvoiceDate < endExcl);
        if (cid != null) invoiceQuery = invoiceQuery.Where(i => i.Tenant.CompanyId == cid);

        var invoices = await invoiceQuery
            .OrderByDescending(i => i.InvoiceDate)
            .ToListAsync();

        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Invoice Status");

        // Headers
        var headers = new[] {
            "Invoice #", "Tenant", "Property", "Unit", "Invoice Date", "Due Date",
            "Amount (KWD)", "Paid Amount (KWD)", "Outstanding (KWD)", "Status", "Days Overdue"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            worksheet.Cells[1, i + 1].Value = headers[i];
        }

        worksheet.Cells[1, 1, 1, headers.Length].Style.Font.Bold = true;
        worksheet.Cells[1, 1, 1, headers.Length].Style.Fill.PatternType = ExcelFillStyle.Solid;
        worksheet.Cells[1, 1, 1, headers.Length].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(59, 130, 246));
        worksheet.Cells[1, 1, 1, headers.Length].Style.Font.Color.SetColor(Color.White);

        int row = 2;
        foreach (var invoice in invoices)
        {
            var daysOverdue = invoice.Status != "Paid" && invoice.DueDate < DateTime.Today
                ? (int)(DateTime.Today - invoice.DueDate).TotalDays
                : 0;

            worksheet.Cells[row, 1].Value = invoice.InvoiceId;
            worksheet.Cells[row, 2].Value = $"{invoice.Tenant.FirstName} {invoice.Tenant.LastName}";
            worksheet.Cells[row, 3].Value = invoice.Lease?.Unit?.Property?.PropertyName ?? "N/A";
            worksheet.Cells[row, 4].Value = invoice.Lease?.Unit?.UnitNumber ?? "N/A";
            worksheet.Cells[row, 5].Value = invoice.InvoiceDate.ToString("yyyy-MM-dd");
            worksheet.Cells[row, 6].Value = invoice.DueDate.ToString("yyyy-MM-dd");
            worksheet.Cells[row, 7].Value = invoice.TotalAmount;
            worksheet.Cells[row, 7].Style.Numberformat.Format = "#,##0.000";
            worksheet.Cells[row, 8].Value = invoice.PaidAmount;
            worksheet.Cells[row, 8].Style.Numberformat.Format = "#,##0.000";
            worksheet.Cells[row, 9].Value = invoice.TotalAmount - invoice.PaidAmount;
            worksheet.Cells[row, 9].Style.Numberformat.Format = "#,##0.000";
            worksheet.Cells[row, 10].Value = invoice.Status;
            worksheet.Cells[row, 11].Value = daysOverdue;

            row++;
        }

        worksheet.Cells.AutoFitColumns();

        var fileName = $"Invoice_Status_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var fileBytes = package.GetAsByteArray();
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    #endregion

    #region Report 5: Payment Report with Filters and Grid

    public async Task<IActionResult> PaymentReport(
        DateTime? fromDate,
        DateTime? toDate,
        int? propertyId,
        string? paymentType,
        string? paymentStatus,
        int? tenantId)
    {
        var startDate = fromDate ?? DateTime.Today.AddMonths(-1);
        var endDate = toDate ?? DateTime.Today;

        // Get properties for filter dropdown
        var properties = await _context.Properties.OrderBy(p => p.PropertyName).ToListAsync();
        ViewBag.Properties = properties;

        // Get tenants for filter dropdown
        var tenants = await _context.Tenants
            .Where(t => t.Status == "Active")
            .OrderBy(t => t.FirstName)
            .ThenBy(t => t.LastName)
            .ToListAsync();
        ViewBag.Tenants = tenants;

        // Build query (end bound is exclusive on the day after `toDate`, so the
        // last selected day is fully included — same rule as every other report).
        var endExcl = EndExclusive(endDate);
        var query = _context.Payments
            .Include(p => p.Lease)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .Include(p => p.Lease)
                .ThenInclude(l => l.Tenant)
            .Where(p => p.PaymentDate >= startDate && p.PaymentDate < endExcl);

        // Company scope — Payments have no EF company filter.
        var cid = ScopedCompanyId();
        if (cid != null)
            query = query.Where(p => p.Tenant.CompanyId == cid);

        // Apply filters
        if (propertyId.HasValue && propertyId.Value > 0)
        {
            query = query.Where(p => p.Lease.Unit.PropertyId == propertyId.Value);
        }

        if (!string.IsNullOrEmpty(paymentType))
        {
            if (Enum.TryParse<PaymentType>(paymentType, true, out var paymentTypeEnum))
            {
                query = query.Where(p => p.PaymentType == paymentTypeEnum);
            }
        }

        if (!string.IsNullOrEmpty(paymentStatus))
        {
            if (Enum.TryParse<PaymentStatus>(paymentStatus, true, out var paymentStatusEnum))
            {
                query = query.Where(p => p.Status == paymentStatusEnum);
            }
        }

        if (tenantId.HasValue && tenantId.Value > 0)
        {
            query = query.Where(p => p.Lease.TenantId == tenantId.Value);
        }

        var payments = await query
            .OrderByDescending(p => p.PaymentDate)
            .ToListAsync();

        // Prepare grid data
        var model = payments.Select(p => new PaymentReportData
        {
            PaymentId = p.PaymentId,
            PaymentDate = p.PaymentDate,
            TenantName = p.Lease?.Tenant != null ? $"{p.Lease.Tenant.FirstName} {p.Lease.Tenant.LastName}" : "N/A",
            PropertyName = p.Lease?.Unit?.Property?.PropertyName ?? "N/A",
            UnitNumber = p.Lease?.Unit?.UnitNumber ?? "N/A",
            Amount = p.AmountPaid,
            PaymentMethod = p.PaymentMethod,
            PaymentStatus = p.PaymentStatus,
            TransactionReference = p.TransactionReference ?? "-",
            CreatedDate = p.CreatedDate
        }).ToList();

        // Summary statistics
        ViewBag.TotalPayments = model.Count;
        ViewBag.TotalAmount = model.Sum(p => p.Amount);
        ViewBag.CompletedCount = model.Count(p => p.PaymentStatus == "Completed");
        ViewBag.CompletedAmount = model.Where(p => p.PaymentStatus == "Completed").Sum(p => p.Amount);
        ViewBag.PendingCount = model.Count(p => p.PaymentStatus == "Pending" || p.PaymentStatus == "PendingVerification");
        ViewBag.PendingAmount = model.Where(p => p.PaymentStatus == "Pending" || p.PaymentStatus == "PendingVerification").Sum(p => p.Amount);

        // Payment type breakdown
        ViewBag.KNETCount = model.Count(p => p.PaymentMethod == "KNET");
        ViewBag.KNETAmount = model.Where(p => p.PaymentMethod == "KNET").Sum(p => p.Amount);
        ViewBag.CashCount = model.Count(p => p.PaymentMethod == "Cash");
        ViewBag.CashAmount = model.Where(p => p.PaymentMethod == "Cash").Sum(p => p.Amount);
        ViewBag.BankTransferCount = model.Count(p => p.PaymentMethod == "BankTransfer");
        ViewBag.BankTransferAmount = model.Where(p => p.PaymentMethod == "BankTransfer").Sum(p => p.Amount);

        // Filter values for form persistence
        ViewBag.FromDate = startDate;
        ViewBag.ToDate = endDate;
        ViewBag.SelectedPropertyId = propertyId;
        ViewBag.SelectedPaymentType = paymentType;
        ViewBag.SelectedPaymentStatus = paymentStatus;
        ViewBag.SelectedTenantId = tenantId;

        return View(model);
    }

    public async Task<IActionResult> PaymentReportExcel(
        DateTime? fromDate,
        DateTime? toDate,
        int? propertyId,
        string? paymentType,
        string? paymentStatus,
        int? tenantId)
    {
        var startDate = fromDate ?? DateTime.Today.AddMonths(-1);
        var endDate = toDate ?? DateTime.Today;
        var endExcl = EndExclusive(endDate);

        var query = _context.Payments
            .Include(p => p.Lease)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .Include(p => p.Lease)
                .ThenInclude(l => l.Tenant)
            .Where(p => p.PaymentDate >= startDate && p.PaymentDate < endExcl);

        // Company scope — Payments have no EF company filter.
        var cid = ScopedCompanyId();
        if (cid != null)
            query = query.Where(p => p.Tenant.CompanyId == cid);

        if (propertyId.HasValue && propertyId.Value > 0)
            query = query.Where(p => p.Lease.Unit.PropertyId == propertyId.Value);

        if (!string.IsNullOrEmpty(paymentType))
        {
            if (Enum.TryParse<PaymentType>(paymentType, true, out var paymentTypeEnum))
            {
                query = query.Where(p => p.PaymentType == paymentTypeEnum);
            }
        }

        if (!string.IsNullOrEmpty(paymentStatus))
        {
            if (Enum.TryParse<PaymentStatus>(paymentStatus, true, out var paymentStatusEnum))
            {
                query = query.Where(p => p.Status == paymentStatusEnum);
            }
        }

        if (tenantId.HasValue && tenantId.Value > 0)
            query = query.Where(p => p.Lease.TenantId == tenantId.Value);

        var payments = await query.OrderByDescending(p => p.PaymentDate).ToListAsync();

        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Payment Report");

        // Headers
        var headers = new[] { "Payment ID", "Date", "Tenant", "Property", "Unit", "Amount (KWD)", "Method", "Status", "Reference" };
        for (int i = 0; i < headers.Length; i++)
        {
            worksheet.Cells[1, i + 1].Value = headers[i];
        }
        worksheet.Cells[1, 1, 1, headers.Length].Style.Font.Bold = true;
        worksheet.Cells[1, 1, 1, headers.Length].Style.Fill.PatternType = ExcelFillStyle.Solid;
        worksheet.Cells[1, 1, 1, headers.Length].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(59, 130, 246));
        worksheet.Cells[1, 1, 1, headers.Length].Style.Font.Color.SetColor(Color.White);

        int row = 2;
        foreach (var p in payments)
        {
            worksheet.Cells[row, 1].Value = p.PaymentId;
            worksheet.Cells[row, 2].Value = p.PaymentDate.ToString("dd/MM/yyyy");
            worksheet.Cells[row, 3].Value = p.Lease?.Tenant != null ? $"{p.Lease.Tenant.FirstName} {p.Lease.Tenant.LastName}" : "N/A";
            worksheet.Cells[row, 4].Value = p.Lease?.Unit?.Property?.PropertyName ?? "N/A";
            worksheet.Cells[row, 5].Value = p.Lease?.Unit?.UnitNumber ?? "N/A";
            worksheet.Cells[row, 6].Value = p.AmountPaid;
            worksheet.Cells[row, 7].Value = p.PaymentMethod;
            worksheet.Cells[row, 8].Value = p.PaymentStatus;
            worksheet.Cells[row, 9].Value = p.TransactionReference ?? "-";
            row++;
        }

        // Format amount column
        worksheet.Column(6).Style.Numberformat.Format = "#,##0.000";
        worksheet.Cells.AutoFitColumns();

        var content = package.GetAsByteArray();
        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"PaymentReport_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.xlsx");
    }

    #endregion
}

#region Data Models

public class PropertyPerformanceData
{
    public string PropertyName { get; set; } = string.Empty;
    public string PACINumber { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public int TotalUnits { get; set; }
    public int OccupiedUnits { get; set; }
    public int VacantUnits { get; set; }
    public decimal OccupancyRate { get; set; }
    public decimal PotentialRent { get; set; }
    public decimal ActualRent { get; set; }
    public decimal CollectionRate { get; set; }
    public decimal OutstandingAmount { get; set; }
    public decimal MaintenanceCost { get; set; }
    public int MaintenanceCount { get; set; }
    public decimal NetRevenue { get; set; }
    public decimal RevenuePerSqFt { get; set; }
}

public class TenantPaymentData
{
    public string TenantName { get; set; } = string.Empty;
    public string PACINumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
    public string UnitNumber { get; set; } = string.Empty;
    public int ActiveLeases { get; set; }
    public decimal MonthlyRent { get; set; }
    public decimal TotalBilled { get; set; }
    public decimal TotalInvoiced { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal OutstandingAmount { get; set; }
    public decimal Outstanding { get; set; }
    public decimal PaymentRate { get; set; }
    public int DaysOverdue { get; set; }
    public decimal LateFees { get; set; }
    public int AvgDaysToPay { get; set; }
    public int TotalPayments { get; set; }
    public DateTime? LastPaymentDate { get; set; }
    public decimal LastPaymentAmount { get; set; }
    public string PaymentScore { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class InvoiceStatusData
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
    public string UnitNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public DateTime DueDate { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public int DaysOverdue { get; set; }
    public string AgingBucket { get; set; } = string.Empty;
}

#endregion

public class PaymentReportData
{
    public int PaymentId { get; set; }
    public DateTime PaymentDate { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
    public string UnitNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string TransactionReference { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
}
