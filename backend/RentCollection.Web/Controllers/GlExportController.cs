using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.Controllers;

/// <summary>
/// Item 9 — General-ledger style CSV export of captured payments.
/// Columns map directly onto QuickBooks Online / Xero CSV imports so the
/// accountant can drop it in without transformation.
/// </summary>
[Authorize(Roles = Roles.Financial)]
public class GlExportController : Controller
{
    private readonly ApplicationDbContext _context;
    public GlExportController(ApplicationDbContext context) { _context = context; }

    /// <summary>
    /// GET /GlExport?start=yyyy-MM-dd&end=yyyy-MM-dd
    /// Returns text/csv with one row per captured payment.
    /// </summary>
    public async Task<IActionResult> Index(DateTime? start, DateTime? end)
    {
        var s = start ?? DateTime.UtcNow.AddMonths(-1).Date;
        var e = end ?? DateTime.UtcNow.Date.AddDays(1);

        var rows = await _context.Payments
            .AsNoTracking()
            .Include(p => p.Tenant)
            .Include(p => p.Lease).ThenInclude(l => l.Unit).ThenInclude(u => u.Property)
            .Include(p => p.Invoice)
            .Where(p => p.PaymentDate >= s && p.PaymentDate <= e
                && p.Status == Core.Enums.PaymentStatus.Completed)
            .OrderBy(p => p.PaymentDate)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("Date,InvoiceNumber,Tenant,Property,Unit,PaymentMethod,Amount,Currency,Reference,Account");
        foreach (var p in rows)
        {
            var tenant = $"{p.Tenant?.FirstName} {p.Tenant?.LastName}".Trim().Replace(",", " ");
            var property = (p.Lease?.Unit?.Property?.PropertyName ?? "").Replace(",", " ");
            var unit = (p.Lease?.Unit?.UnitNumber ?? "").Replace(",", " ");
            sb.AppendLine(string.Join(',',
                p.PaymentDate.ToString("yyyy-MM-dd"),
                p.InvoiceId.HasValue ? $"INV-{p.InvoiceId:D6}" : "",
                tenant,
                property,
                unit,
                p.PaymentType,
                p.AmountPaid.ToString("0.000"),
                "KWD",
                p.TransactionReference ?? "",
                "Rental Income"));
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()),
            "text/csv",
            $"gl-export-{s:yyyyMMdd}-{e:yyyyMMdd}.csv");
    }

    /// <summary>
    /// Item 9 — Owner statement. Per-property P&L over a window, suitable for
    /// emailing to property owners. CSV output keeps it provider-agnostic.
    /// </summary>
    public async Task<IActionResult> OwnerStatement(DateTime? start, DateTime? end, int? propertyId)
    {
        var s = start ?? DateTime.UtcNow.AddMonths(-1).Date;
        var e = end ?? DateTime.UtcNow.Date.AddDays(1);

        var payments = _context.Payments
            .AsNoTracking()
            .Include(p => p.Lease).ThenInclude(l => l.Unit).ThenInclude(u => u.Property)
            .Where(p => p.PaymentDate >= s && p.PaymentDate <= e
                && p.Status == Core.Enums.PaymentStatus.Completed);
        if (propertyId.HasValue) payments = payments.Where(p => p.PropertyId == propertyId.Value);

        var grouped = (await payments.ToListAsync())
            .GroupBy(p => new { p.PropertyId, Name = p.Lease?.Unit?.Property?.PropertyName ?? "?" })
            .Select(g => new
            {
                g.Key.PropertyId,
                g.Key.Name,
                Count = g.Count(),
                Gross = g.Sum(p => p.AmountPaid),
            })
            .OrderBy(g => g.Name)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Owner Statement {s:yyyy-MM-dd} to {e:yyyy-MM-dd}");
        sb.AppendLine("Property,Payments,GrossKWD");
        foreach (var row in grouped)
            sb.AppendLine($"{row.Name.Replace(",", " ")},{row.Count},{row.Gross:0.000}");
        sb.AppendLine($"Total,{grouped.Sum(r => r.Count)},{grouped.Sum(r => r.Gross):0.000}");

        return File(Encoding.UTF8.GetBytes(sb.ToString()),
            "text/csv",
            $"owner-statement-{s:yyyyMMdd}-{e:yyyyMMdd}.csv");
    }
}
