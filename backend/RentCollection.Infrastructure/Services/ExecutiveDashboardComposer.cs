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

    public async Task<ExecutiveDashboardPdfModel> BuildAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var today = DateTime.Today;
        var startDate = from ?? new DateTime(today.Year, today.Month, 1);
        var endDate = to ?? today;
        var rangeDays = Math.Max(1, (int)(endDate - startDate).TotalDays + 1);
        var prevEnd = startDate.AddDays(-1);
        var prevStart = prevEnd.AddDays(-(rangeDays - 1));

        var collected = await _context.Payments
            .Where(p => p.PaymentDate >= startDate && p.PaymentDate <= endDate
                        && p.Status == PaymentStatus.Completed)
            .SumAsync(p => (decimal?)p.AmountPaid, ct) ?? 0m;
        var prevCollected = await _context.Payments
            .Where(p => p.PaymentDate >= prevStart && p.PaymentDate <= prevEnd
                        && p.Status == PaymentStatus.Completed)
            .SumAsync(p => (decimal?)p.AmountPaid, ct) ?? 0m;
        var outstanding = await _context.Invoices
            .Where(i => i.Status != "Paid")
            .SumAsync(i => (decimal?)(i.TotalAmount - i.PaidAmount), ct) ?? 0m;
        var overdueCount = await _context.Invoices
            .CountAsync(i => i.Status != "Paid" && i.DueDate < today, ct);
        var totalUnits = await _context.Units.CountAsync(ct);
        var occupiedUnits = await _context.Units.CountAsync(u => u.Status == "Occupied", ct);
        var occupancy = totalUnits > 0 ? (decimal)occupiedUnits / totalUnits * 100m : 0m;

        var trendStart = new DateTime(today.Year, today.Month, 1).AddMonths(-11);
        var monthly = await _context.Payments
            .Where(p => p.PaymentDate >= trendStart && p.PaymentDate <= today
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

        var statusRaw = await _context.Invoices
            .GroupBy(i => i.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var pendingButOverdue = await _context.Invoices
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

        var topRaw = await _context.Payments
            .Where(p => p.PaymentDate >= startDate && p.PaymentDate <= endDate
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
