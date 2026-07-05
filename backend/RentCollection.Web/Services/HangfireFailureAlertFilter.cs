using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.Configuration;
using RentCollection.Application.Interfaces;

namespace RentCollection.Web.Services;

/// <summary>
/// Hangfire state filter — emails admins when a recurring job exhausts its
/// retries and lands in <see cref="FailedState"/>. Without this, a busted
/// monthly-invoice run is silent and the operator discovers it on the 2nd of
/// the month when nobody got billed.
///
/// Recipient list comes from <c>Reports:MonthlyEmailRecipients</c> (reused
/// from the executive-dashboard job) or, when blank, the first SuperAdmin in
/// the database. Failures during the email send itself are swallowed —
/// nothing should break the Hangfire pipeline.
/// </summary>
public class HangfireFailureAlertFilter : JobFilterAttribute, IApplyStateFilter
{
    /// <summary>
    /// Root IServiceProvider, set once at app startup by Program.cs. Filter
    /// callbacks run outside any request scope, so we build our own when we
    /// need scoped services like IEmailService.
    /// </summary>
    public static IServiceProvider? RootServices { get; set; }

    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        if (context.NewState is not FailedState failed) return;
        if (RootServices == null) return;

        using var scope = RootServices.CreateScope();
        var sp = scope.ServiceProvider;
        if (sp.GetService(typeof(IEmailService)) is not IEmailService email) return;
        if (sp.GetService(typeof(IConfiguration)) is not IConfiguration config) return;

        var recipients = (config["Reports:MonthlyEmailRecipients"] ?? "")
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .ToList();
        if (recipients.Count == 0) return;

        var jobId = context.BackgroundJob.Id;
        var jobName = context.BackgroundJob.Job?.Method?.Name ?? "(unknown job)";
        var html = $@"
            <h3 style='color:#dc2626'>Hangfire job failed</h3>
            <p><strong>{jobName}</strong> (job #{jobId}) exhausted retries at
            {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC.</p>
            <p><strong>Exception:</strong></p>
            <pre style='background:#fef2f2;padding:0.7rem;border-radius:6px;'>{failed.Exception?.Message ?? "(no exception details)"}</pre>
            <p>Visit the Hangfire dashboard for the full stack trace and
            'Retry' button.</p>";

        // Fire-and-forget — Hangfire filter callbacks should never throw.
        foreach (var to in recipients)
        {
            try { _ = email.SendAsync(to, $"[HF] Job failed: {jobName}", html); }
            catch { /* swallow */ }
        }
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction) { }
}
