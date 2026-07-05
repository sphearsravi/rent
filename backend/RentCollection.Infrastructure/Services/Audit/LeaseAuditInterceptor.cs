using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using RentCollection.Core.Entities;

namespace RentCollection.Infrastructure.Services.Audit;

/// <summary>
/// Catches Lease edits on the way out of <c>SaveChangesAsync</c> and writes
/// a single <c>AuditLog</c> row per save with a JSON diff of the changed
/// fields. Without this, the History page would never see edits — only
/// lifecycle actions (Renew / Terminate / Transfer / Delete) currently call
/// <see cref="IAuditLogService"/> directly.
///
/// Tracked fields are the operationally interesting ones; <c>Status</c> is
/// intentionally skipped because lifecycle actions already audit transitions
/// and we don't want double-logging. <c>ModifiedDate</c>, <c>RowVersion</c>,
/// and the renewal back-links are skipped as noise.
///
/// The audit row is appended to the same change-tracker before the save
/// proceeds, so it commits atomically with the user-driven update.
/// </summary>
public class LeaseAuditInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    /// <summary>
    /// Property names on <see cref="Lease"/> whose changes are operationally
    /// interesting. Status is excluded — see class doc.
    /// </summary>
    private static readonly HashSet<string> TrackedFields = new(StringComparer.Ordinal)
    {
        nameof(Lease.TenantId),
        nameof(Lease.UnitId),
        nameof(Lease.LeaseStartDate),
        nameof(Lease.LeaseEndDate),
        nameof(Lease.MonthlyRent),
        nameof(Lease.SecurityDeposit),
        nameof(Lease.PaymentDueDay),
        nameof(Lease.LeaseType),
        nameof(Lease.LateFeeAmount),
        nameof(Lease.LateFeeGracePeriod),
    };

    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<LeaseAuditInterceptor> _logger;

    public LeaseAuditInterceptor(
        IHttpContextAccessor? httpContextAccessor,
        ILogger<LeaseAuditInterceptor> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var ctx = eventData.Context;
        if (ctx == null) return base.SavingChangesAsync(eventData, result, cancellationToken);

        var byUser = _httpContextAccessor?.HttpContext?.User?.Identity?.Name ?? "system";

        // Snapshot the lease entries before iterating: appending an
        // AuditLog to the change-tracker is safe (different entity type)
        // but we need to capture the candidate list first to keep
        // enumeration stable across the loop.
        var modifiedLeases = ctx.ChangeTracker
            .Entries<Lease>()
            .Where(e => e.State == EntityState.Modified)
            .ToList();

        foreach (var entry in modifiedLeases)
        {
            var diff = BuildDiff(entry);
            if (diff.Count == 0) continue; // only ModifiedDate/RowVersion changed

            var payload = JsonSerializer.Serialize(new { changes = diff }, JsonOpts);

            ctx.Set<AuditLog>().Add(new AuditLog
            {
                EntityType = "Lease",
                EntityId = entry.Entity.LeaseId,
                Action = "Edited",
                ByUser = string.IsNullOrWhiteSpace(byUser) ? "system" : byUser,
                At = DateTime.UtcNow,
                PayloadJson = payload,
            });
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Build a <c>{ field: [old, new] }</c> dictionary for the tracked
    /// fields on a modified lease entry. Exposed as internal so unit tests
    /// can validate the diff without spinning up a full DbContext.
    /// </summary>
    internal static Dictionary<string, object?[]> BuildDiff(EntityEntry<Lease> entry)
    {
        var diff = new Dictionary<string, object?[]>();
        foreach (var prop in entry.Properties)
        {
            var name = prop.Metadata.Name;
            if (!TrackedFields.Contains(name)) continue;
            if (!prop.IsModified) continue;

            var oldVal = prop.OriginalValue;
            var newVal = prop.CurrentValue;
            // EF sometimes flags IsModified=true even when values match
            // (e.g. after an explicit Update() with no real change). Skip
            // those so the audit doesn't fill with noise.
            if (Equals(oldVal, newVal)) continue;

            diff[name] = new[] { Normalize(oldVal), Normalize(newVal) };
        }
        return diff;
    }

    /// <summary>
    /// Make values JSON-friendly: dates as ISO 8601 date-only, decimals as
    /// numbers, everything else passes through. Keeps the payload compact
    /// and grep-friendly.
    /// </summary>
    private static object? Normalize(object? v) => v switch
    {
        null => null,
        DateTime dt => dt.ToString("yyyy-MM-dd"),
        decimal d => d,
        _ => v,
    };
}
