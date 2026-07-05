using RentCollection.Core.Entities;

namespace RentCollection.Web.Models;

public class DashboardViewModel
{
    public int TotalProperties { get; set; }
    public int TotalUnits { get; set; }
    public int OccupiedUnits { get; set; }
    public int VacantUnits => TotalUnits - OccupiedUnits;
    public double OccupancyRate => TotalUnits > 0 ? Math.Round((double)OccupiedUnits / TotalUnits * 100, 1) : 0;
    public int TotalTenants { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal PendingPayments { get; set; }
    public int MaintenanceRequests { get; set; }
    public List<Payment> RecentPayments { get; set; } = new();
    public List<Lease> UpcomingLeaseExpirations { get; set; } = new();

    // ── Things needing attention TODAY (Dashboard revamp) ──
    //
    // Each property below answers a "what should I act on now?" question for
    // the operator landing on /Dashboard. Counts are intentionally cheap
    // (each is a single COUNT or simple SUM); the page strip uses them to
    // build clickable cards that deep-link to filtered list pages.
    public int OverdueInvoicesCount { get; set; }
    public decimal OverdueAmount { get; set; }
    public int LeasesExpiringIn30Days { get; set; }
    public int PendingLeasesAwaitingActivation { get; set; }
    public int PendingPaymentApprovals { get; set; }
    public int DepositsAwaitingPayment { get; set; }
    public int VacantUnitsAvailableSoon { get; set; }   // available within 30d

    /// <summary>Recent activity feed pulled from AuditLog — last 8 events
    /// across every entity type. Drives the right-hand activity card.</summary>
    public List<AuditLog> RecentActivity { get; set; } = new();
}
