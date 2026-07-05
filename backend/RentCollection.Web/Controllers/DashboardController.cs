using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Infrastructure.Data;
using RentCollection.Web.Models;

namespace RentCollection.Web.Controllers;

public class DashboardController : Controller
{
    private readonly ApplicationDbContext _context;

    public DashboardController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        try
        {
            // NOTE: removed a CanConnectAsync() probe here — it was an extra remote
            // round-trip on every dashboard load (and, with no command timeout, could
            // itself hang). If the DB is truly down the first real query below throws
            // and is handled by the same catch.
            var dashboardData = new DashboardViewModel
            {
                TotalProperties = await _context.Properties.CountAsync(),
                TotalUnits = await _context.Units.CountAsync(),
                OccupiedUnits = await _context.Units.CountAsync(u => u.Status == "Occupied"),
                TotalTenants = await _context.Tenants.CountAsync(t => t.Status == "Active"),
                TotalRevenue = await _context.Payments
                    .Where(p => p.PaymentDate.Month == DateTime.UtcNow.Month &&
                               p.PaymentDate.Year == DateTime.UtcNow.Year)
                    .SumAsync(p => p.AmountPaid),
                PendingPayments = await _context.Invoices
                    .Where(i => i.Status == "Pending")
                    .SumAsync(i => i.TotalAmount - i.PaidAmount),
                MaintenanceRequests = await _context.MaintenanceRequests
                    .Where(m => m.Status == "Open" || m.Status == "In Progress")
                    .CountAsync(),
                RecentPayments = await _context.Payments
                    .Include(p => p.Tenant)
                    .Include(p => p.Lease)
                        .ThenInclude(l => l.Unit)
                            .ThenInclude(u => u.Property)
                    .OrderByDescending(p => p.PaymentDate)
                    .Take(5)
                    .ToListAsync(),
                UpcomingLeaseExpirations = await _context.Leases
                    .Include(l => l.Tenant)
                    .Include(l => l.Unit)
                        .ThenInclude(u => u.Property)
                    .Where(l => l.LeaseEndDate >= DateTime.UtcNow &&
                               l.LeaseEndDate <= DateTime.UtcNow.AddDays(30) &&
                               l.Status == "Active")
                    .OrderBy(l => l.LeaseEndDate)
                    .Take(5)
                    .ToListAsync(),

                // ── Action items (Dashboard revamp). Cheap counts so the
                // page renders fast even on big tenants. Each card on the
                // strip deep-links to the matching filtered list view. ──
                OverdueInvoicesCount = await _context.Invoices
                    .CountAsync(i => i.Status == "Overdue"),
                OverdueAmount = await _context.Invoices
                    .Where(i => i.Status == "Overdue")
                    .SumAsync(i => (decimal?)(i.TotalAmount - i.PaidAmount)) ?? 0m,
                LeasesExpiringIn30Days = await _context.Leases
                    .CountAsync(l => l.Status == "Active"
                        && l.LeaseEndDate >= DateTime.UtcNow
                        && l.LeaseEndDate <= DateTime.UtcNow.AddDays(30)),
                PendingLeasesAwaitingActivation = await _context.Leases
                    .CountAsync(l => l.Status == "Pending"),
                PendingPaymentApprovals = await _context.BankTransfers
                    .CountAsync(b => b.Status == RentCollection.Core.Enums.BankTransferStatus.PendingVerification),
                DepositsAwaitingPayment = await _context.Invoices
                    .CountAsync(i => i.InvoiceType == "Deposit"
                        && i.Status != "Paid"
                        && i.Status != "Cancelled"),
                VacantUnitsAvailableSoon = await _context.Units
                    .CountAsync(u => u.Status == "Vacant"
                        && (u.AvailableFromDate == null
                            || u.AvailableFromDate <= DateTime.UtcNow.AddDays(30))),

                RecentActivity = await _context.AuditLogs
                    .OrderByDescending(a => a.At)
                    .Take(8)
                    .ToListAsync(),
            };

            return View(dashboardData);
        }
        catch (Exception)
        {
            // Database not configured yet, show demo data
            return View(new DashboardViewModel
            {
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
