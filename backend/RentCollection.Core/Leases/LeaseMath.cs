namespace RentCollection.Core.Leases;

/// <summary>
/// Single source of truth for lease arithmetic that used to live in three
/// places (InvoicesController, RecurringJobs, Views/Leases/Index.cshtml) and
/// drifted. Pure functions — no DB, no time-zone surprises.
///
/// Introduced for LEASE_AUDIT Flaws #6 (PaymentDueDay overflow) and #14
/// (days-remaining using UtcNow vs date-only LeaseEndDate).
/// </summary>
public static class LeaseMath
{
    /// <summary>
    /// Returns the due date for a lease's invoice in a specific (year, month),
    /// clamped to the month's last day when PaymentDueDay overflows.
    ///
    /// Examples: <c>ComputeDueDate(31, 2026, 2)</c> → 2026-02-28
    /// (or -29 in a leap year), <c>ComputeDueDate(31, 2026, 4)</c> → 2026-04-30.
    /// </summary>
    public static DateTime ComputeDueDate(int paymentDueDay, int year, int month)
    {
        var safeDay = Math.Clamp(paymentDueDay, 1, DateTime.DaysInMonth(year, month));
        return new DateTime(year, month, safeDay);
    }

    /// <summary>
    /// Whole-day difference between today and the lease end date. Uses
    /// <see cref="DateTime.Today"/> and the <c>.Date</c> component of
    /// <paramref name="leaseEndDate"/> so the answer doesn't oscillate by ±1
    /// across midnight UTC.
    ///
    /// Past-due → negative.
    /// </summary>
    public static int DaysRemaining(DateTime leaseEndDate)
        => (leaseEndDate.Date - DateTime.Today).Days;

    /// <summary>
    /// True when today is strictly after the lease end date. The daily
    /// <c>LeaseExpiry</c> job uses this to flip Active leases to Expired.
    /// </summary>
    public static bool IsPastEnd(DateTime leaseEndDate)
        => DateTime.Today > leaseEndDate.Date;
}
