using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RentCollection.Web.Health;

/// <summary>
/// Liveness probe for the Hangfire background worker. Returns Healthy iff at
/// least one server has heartbeated to the storage backend in the last 60 s.
/// A failing check is the difference between "the rent invoice job has been
/// failing for 3 days" and "the rent invoice job has been not-running for 3
/// days" — both equally bad, both invisible without this probe.
///
/// Surfaced at <c>/health</c> by the health-checks middleware. Read by the
/// post-deploy smoke script (Sprint 1) and any future uptime monitor.
/// </summary>
public class HangfireHealthCheck : IHealthCheck
{
    /// <summary>How fresh the last server heartbeat must be to count as alive.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(1);

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var monitor = JobStorage.Current.GetMonitoringApi();
            var servers = monitor.Servers();
            if (servers.Count == 0)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "No Hangfire servers registered. The scheduler is down."));
            }

            var newest = servers.Max(s => s.Heartbeat ?? s.StartedAt);
            var age = DateTime.UtcNow - newest;
            if (age > StaleAfter)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Newest Hangfire heartbeat is {age.TotalSeconds:0}s old (threshold {StaleAfter.TotalSeconds:0}s). " +
                    "The scheduler may be hung."));
            }

            var data = new Dictionary<string, object>
            {
                ["servers"] = servers.Count,
                ["lastHeartbeatAgeSeconds"] = age.TotalSeconds,
                ["enqueued"] = monitor.EnqueuedCount("default"),
                ["failed"] = monitor.FailedCount(),
            };
            return Task.FromResult(HealthCheckResult.Healthy(
                $"Hangfire OK ({servers.Count} server(s), last heartbeat {age.TotalSeconds:0}s ago)",
                data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Hangfire storage probe threw.", ex));
        }
    }
}
