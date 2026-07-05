namespace RentCollection.Core.Entities;

/// <summary>
/// One row per fiscal period that has been formally closed via
/// <c>/Reports/MonthEndClose</c>. Acts as the "the books are shut for
/// this month" marker — re-run buttons on the close page hide for closed
/// periods so an admin can't accidentally re-bill.
///
/// <see cref="SnapshotJson"/> captures the close-state aggregator's
/// payload at the moment of close so a year-end audit can reconstruct
/// what the dashboard looked like when it was signed off.
/// </summary>
public class ClosedPeriod
{
    public int ClosedPeriodId { get; set; }

    /// <summary>
    /// Period identifier in <c>yyyy-MM</c> format (e.g. <c>"2026-06"</c>).
    /// Indexed unique — at most one close per period.
    /// </summary>
    public string Period { get; set; } = string.Empty;

    /// <summary>UTC instant the close was confirmed.</summary>
    public DateTime ClosedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User who clicked the close button.</summary>
    public string ClosedBy { get; set; } = string.Empty;

    /// <summary>
    /// JSON snapshot of the close-state aggregator response at the moment
    /// of closing. Lets a future auditor see exactly what each of the 9
    /// steps reported when the admin signed off.
    /// </summary>
    public string? SnapshotJson { get; set; }

    /// <summary>Optional close-out note from the operator.</summary>
    public string? Notes { get; set; }
}
