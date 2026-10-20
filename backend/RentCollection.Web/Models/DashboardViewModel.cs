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
    /// across every entity type. No longer rendered on the owner dashboard
    /// (kept for any other consumer of this model).</summary>
    public List<AuditLog> RecentActivity { get; set; } = new();

    // ── Owner dashboard redesign (Oct 2026) ──────────────────────────────
    // Header
    public string? FirstName { get; set; }
    public string? CompanyName { get; set; }
    /// <summary>Localizer key: "Good morning" / "Good afternoon" / "Good evening",
    /// chosen by server local hour.</summary>
    public string GreetingKey { get; set; } = "Good morning";

    // Money row
    public decimal CollectedThisMonth => TotalRevenue;
    public decimal ExpectedThisMonth { get; set; }
    public double CollectionRate => ExpectedThisMonth > 0
        ? Math.Round((double)(CollectedThisMonth / ExpectedThisMonth) * 100, 0)
        : 0;
    public int DaysLeftInMonth { get; set; }
    public decimal OutstandingAmount => PendingPayments;
    public int OutstandingInvoiceCount { get; set; }
    public int OverdueInvoiceCount => OverdueInvoicesCount;
    public int OverdueTenantCount { get; set; }
    public decimal OverdueOver30Amount { get; set; }
    public int OverdueOver30TenantCount { get; set; }
    public decimal VacancyCostPerMonth { get; set; }

    // Attention queue
    public int PaymentsToVerifyCount { get; set; }
    public decimal PaymentsToVerifyAmount { get; set; }
    public int PaymentsToVerifyCashCount { get; set; }
    public int PaymentsToVerifyBankCount { get; set; }
    public int MaintenanceOpenCount => MaintenanceRequests;
    public int MaintenanceSlaBreachedCount { get; set; }
    public int LongestVacantDays { get; set; }
    public string? LongestVacantUnitLabel { get; set; }
    public int AttentionItemCount =>
        (PaymentsToVerifyCount > 0 ? 1 : 0) +
        (OverdueTenantCount > 0 ? 1 : 0) +
        (PendingLeasesAwaitingActivation > 0 ? 1 : 0) +
        (LeasesExpiringIn30Days > 0 ? 1 : 0) +
        (MaintenanceOpenCount > 0 ? 1 : 0) +
        (VacantUnits > 0 ? 1 : 0);

    // Collections trend (last 6 months, oldest → current)
    public List<MonthPoint> CollectionTrend { get; set; } = new();
    public double AvgCollectionRate { get; set; }
    public string? BestMonthLabel { get; set; }
    public double BestMonthRate { get; set; }
    public double AvgDaysToPay { get; set; }

    // Property health (weakest collection first, max 10)
    public List<PropertyHealthRow> PropertyHealth { get; set; } = new();

    // Who owes the most (top 5)
    public List<ArrearsRow> TopArrears { get; set; } = new();

    // Leases ending soon — sum of MonthlyRent across UpcomingLeaseExpirations
    public decimal RentRollAtRisk { get; set; }
}

/// <summary>One month on the collections trend chart. Bar heights are
/// pre-computed server-side as integer px out of 160 so the view stays dumb.</summary>
public class MonthPoint
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal Expected { get; set; }
    public decimal Collected { get; set; }
    public bool IsCurrent { get; set; }
    public int ExpectedPx { get; set; }
    public int CollectedPx { get; set; }
    public double Rate => Expected > 0 ? Math.Min(100, Math.Round((double)(Collected / Expected) * 100, 0)) : 0;
}

public class PropertyHealthRow
{
    public int PropertyId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public int TotalUnits { get; set; }
    public int OccupiedUnits { get; set; }
    public decimal RentRoll { get; set; }
    public decimal Collected { get; set; }
    public double CollectionRate { get; set; }
    public decimal OverdueAmount { get; set; }
}

public class ArrearsRow
{
    public int TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
    public string UnitNumber { get; set; } = string.Empty;
    public int DaysLate { get; set; }
    public decimal Amount { get; set; }
}
