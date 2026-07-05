using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RentCollection.Web;

/// <summary>
/// Sprint 5 A7 — structured JSON body for the /health endpoints. Matches the
/// shape orchestrators (Kubernetes, Azure Container Apps, Cloud Run) expect
/// to be parseable, and gives operators per-check detail instead of a
/// terse "Healthy" / "Unhealthy" string.
/// </summary>
internal static class HealthCheckJsonWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static Task Write(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                durationMs = e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description,
                tags = e.Value.Tags,
                error = e.Value.Exception?.Message,
            }),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, Options));
    }
}
