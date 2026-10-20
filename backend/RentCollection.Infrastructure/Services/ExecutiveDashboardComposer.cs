using Microsoft.EntityFrameworkCore;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Enums;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Infrastructure.Services;

/// <summary>
/// Single source of truth for the executive-dashboard aggregations. Mirrors
/// <c>ReportsController.Snapshot</c>'s shape so the on-screen JSON and the
/// printed PDF can never drift.
/// </summary>
public class ExecutiveDashboardComposer : IExecutiveDashboardComposer
{
    private readonly ApplicationDbContext _context;

    public ExecutiveDashboardComposer(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<ExecutiveDashboardPdfModel> BuildAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default)
        => BuildAsync(from, to, companyId: null, ct);

    public async Task<ExecutiveDashboardPdfModel> BuildAsync(
        DateTime? from, DateTime? to, int? companyId, CancellationToken ct = default)
    {
        var today = DateTime.Today;
        var startDate = from ?? new DateTime(today.Year, today.Month, 1);
        var endDate = to ?? today;
        // PaymentDate carries a time component, so `<= endDate` (midnight) silently
        // dropped every payment made ON the last day of the period. Use an
        // exclusive upper bound at the start of the following day instead.
        var endExclusive = endDate.Date.AddDays(1);
        var rangeDays = Math.Max(1, (int)(endDate.Date - startDate.Date).TotalDays + 1);
        var prevEnd = startDate.Date.AddDays(-1);
        var prevStart = prevEnd.AddDays(-(rangeDays - 1));
        var prevEndExclusive = prevEnd.AddDays(1);
        var todayExclusive = today.AddDays(1);

        // Company scope. null = system-wide (SuperAdmin / ops mailing list).
        // Payments/Invoices/Leases are scoped through their Tenant; Units through
        // their Property — these are the only CompanyId carriers in the model.
        var payments = companyId.HasValue
            ? _context.Payments.Where(p => p.Tenant.CompanyId == companyId.Value)
            : _context.Payments;
        var invoices = companyId.HasValue
            ? _context.Invoices.Where(i => i.Tenant.CompanyId == companyId.Value)
            : _context.Invoices;
        var units = companyId.HasValue
            ? _context.Units.Where(u => u.Property.CompanyId == companyId.Value)
            : _context.Units;

        var collected = await payments
            .Where(p => p.PaymentDate >= startDate && p.PaymentDate < endExclusive
                        && p.Status == PaymentStatus.Completed)
            .SumAsync(p => (decimal?)p.AmountPaid, ct) ?? 0m;
        var prevCollected = await payments
            .Where(p => p.PaymentDate >= prevStart && p.PaymentDate < prevEndExclusive
                        && p.Status == PaymentStatus.Completed)
            .SumAsync(p => (decimal?)p.AmountPaid, ct) ?? 0m;
        // Cancelled invoices are not receivables — exclude them from outstanding
        // and overdue so a voided invoice can't inflate the figures forever.
        var outstanding = await invoices
            .Where(i => i.Status != InvoiceStatuses.Paid && i.Status != InvoiceStatuses.Cancelled)
            .SumAsync(i => (decimal?)(i.TotalAmount - i.PaidAmount), ct) ?? 0m;
        var overdueCount = await invoices
            .CountAsync(i => i.Status != InvoiceStatuses.Paid && i.Status != InvoiceStatuses.Cancelled
                             && i.DueDate < today, ct);
        var totalUnits = await units.CountAsync(ct);
        var occupiedUnits = await units.CountAsync(u => u.Status == "Occupied", ct);
        var occupancy = totalUnits > 0 ? (decimal)occupiedUnits / totalUnits * 100m : 0m;

        var trendStart = new DateTime(today.Year, today.Month, 1).AddMonths(-11);
        var monthly = await payments
            .Where(p => p.PaymentDate >= trendStart && p.PaymentDate < todayExclusive
                        && p.Status == PaymentStatus.Completed)
            .GroupBy(p => new { p.PaymentDate.Year, p.PaymentDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(p => p.AmountPaid) })
            .ToListAsync(ct);
        var trend = new List<(string, decimal)>();
        for (int i = 0; i < 12; i++)
        {
            var m = trendStart.AddMonths(i);
            var hit = monthly.FirstOrDefault(x => x.Year == m.Year && x.Month == m.Month);
            trend.Add((m.ToString("MMM yy"), hit?.Total ?? 0m));
        }

        var statusRaw = await invoices
            .GroupBy(i => i.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var pendingButOverdue = await invoices
            .CountAsync(i => i.Status == "Pending" && i.DueDate < today, ct);
        var status = new List<(string, int)>();
        foreach (var s in statusRaw)
        {
            if (s.Key == "Pending")
            {
                status.Add(("Pending", s.Count - pendingButOverdue));
                status.Add(("Overdue", pendingButOverdue));
            }
            else status.Add((s.Key, s.Count));
        }

        var topRaw = await payments
            .Where(p => p.PaymentDate >= startDate && p.PaymentDate < endExclusive
                        && p.Status == PaymentStatus.Completed && p.PropertyId != 0)
            .GroupBy(p => p.PropertyId)
            .Select(g => new { PropertyId = g.Key, Total = g.Sum(x => x.AmountPaid) })
            .OrderByDescending(x => x.Total)
            .Take(5)
            .ToListAsync(ct);
        var topIds = topRaw.Select(x => x.PropertyId).ToList();
        var names = await _context.Properties
            .Where(p => topIds.Contains(p.PropertyId))
            .Select(p => new { p.PropertyId, p.PropertyName })
            .ToDictionaryAsync(p => p.PropertyId, p => p.PropertyName, ct);
        var topProps = topRaw
            .Select(x => (names.GetValueOrDefault(x.PropertyId) ?? "—", x.Total))
            .ToList();

        decimal Delta(decimal cur, decimal prev) =>
            prev == 0 ? (cur == 0 ? 0 : 100) : Math.Round((cur - prev) / prev * 100m, 1);

        return new ExecutiveDashboardPdfModel
        {
            FromDate = startDate,
            ToDate = endDate,
            Collected = collected,
            Outstanding = outstanding,
            OverdueCount = overdueCount,
            OccupancyPercent = occupancy,
            TotalUnits = totalUnits,
            OccupiedUnits = occupiedUnits,
            CollectedDelta = Delta(collected, prevCollected),
            Trend = trend,
            StatusBreakdown = status,
            TopProperties = topProps,
            Currency = "KWD",
        };
    }
}
