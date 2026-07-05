namespace RentCollection.Application.Interfaces;

/// <summary>
/// The canonical set of Hangfire recurring-job IDs. Single source of truth
/// shared by:
///   • <c>Program.cs</c> — when calling <c>IRecurringJobManager.AddOrUpdate</c>
///     to register each job's schedule.
///   • <c>ReportsController.TriggerJob</c> — to allow-list which IDs an
///     operator can re-fire from the Month-End Close checklist.
///   • The MEC view JS — passes one of these strings via <c>data-job</c>.
///
/// Convention: kebab-case IDs that map 1:1 to a <see cref="RecurringJobMethod"/>
/// method name on <c>RecurringJobs</c> (e.g. <c>flip-expired-leases</c> →
/// <c>FlipExpiredLeasesAsync</c>). The reflection test in
/// <c>RecurringJobIdsTests</c> proves this mapping holds for every entry.
///
/// Adding a new recurring job: add the ID here, add a method on
/// <c>RecurringJobs</c>, register it in <c>Program.cs</c>. The compile + the
/// reflection test will tell you if you missed one.
/// </summary>
public static class RecurringJobIds
{
    public const string GenerateMonthlyInvoices       = "generate-monthly-invoices";
    public const string FlipOverdueInvoices           = "flip-overdue-invoices";
    public const string FlipExpiredLeases             = "flip-expired-leases";
    public const string ReconcileUnitStatus           = "reconcile-unit-status";
    public const string MonthlyExecutiveDashboardEmail = "monthly-executive-dashboard-email";
    public const string LeaseExpiryReminders          = "lease-expiry-reminders";
    public const string ProcessNotificationOutbox     = "process-notification-outbox";

    /// <summary>
    /// All known IDs in one array — iterate for "is this allowed?" checks
    /// and for the reflection test.
    /// </summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        GenerateMonthlyInvoices,
        FlipOverdueInvoices,
        FlipExpiredLeases,
        ReconcileUnitStatus,
        MonthlyExecutiveDashboardEmail,
        LeaseExpiryReminders,
        ProcessNotificationOutbox,
    };

    /// <summary>
    /// Map from job ID to the <c>RecurringJobs</c> method that implements it.
    /// Used by the reflection test to assert each ID is wired to real code,
    /// and could be used by a future generic <c>TriggerJob</c> dispatcher.
    /// </summary>
    public static string MethodNameFor(string jobId) => jobId switch
    {
        GenerateMonthlyInvoices            => "GenerateMonthlyInvoicesAsync",
        FlipOverdueInvoices                => "FlipOverdueInvoicesAsync",
        FlipExpiredLeases                  => "FlipExpiredLeasesAsync",
        ReconcileUnitStatus                => "ReconcileUnitStatusAsync",
        MonthlyExecutiveDashboardEmail     => "SendMonthlyExecutiveDashboardAsync",
        LeaseExpiryReminders               => "LeaseExpiryRemindersAsync",
        ProcessNotificationOutbox          => "ProcessNotificationOutboxAsync",
        _ => throw new ArgumentException($"Unknown recurring job ID: {jobId}", nameof(jobId)),
    };
}
