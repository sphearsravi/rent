using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using RentCollection.Core.Entities;

namespace RentCollection.Infrastructure.Services.Audit;

/// <summary>
/// Single multi-entity audit interceptor. Catches modifications to Tenant,
/// Property, and Unit on the way out of <c>SaveChangesAsync</c> and writes
/// one <see cref="AuditLog"/> row per save with a JSON diff of the changed
/// fields. Lease has its own dedicated interceptor (kept for its larger
/// tracked-field set + edit-vs-lifecycle deduplication).
///
/// Each entity exposes a small <c>TrackedFields</c> set so noise (audit
/// metadata, FK back-refs, RowVersion) doesn't drown the signal.
/// </summary>
public class EntityAuditInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    /// <summary>Per-entity-type tracked field set. Skip everything else.</summary>
    private static readonly Dictionary<Type, HashSet<string>> Tracked = new()
    {
        [typeof(Tenant)] = new(StringComparer.Ordinal)
        {
            nameof(Tenant.FirstName),
            nameof(Tenant.LastName),
            nameof(Tenant.Email),
            nameof(Tenant.Phone),
            nameof(Tenant.GovernmentId),
            nameof(Tenant.PACINumber),
            nameof(Tenant.BuildingPACINumber),
            nameof(Tenant.Status),
            nameof(Tenant.CompanyId),
            nameof(Tenant.EmploymentStatus),
            // Skip: Password (already covered by CredentialIssued), IsDeleted (covered by Forget/Restore)
        },
        [typeof(Property)] = new(StringComparer.Ordinal)
        {
            nameof(Property.PropertyName),
            nameof(Property.PACINumber),
            nameof(Property.Address),
            nameof(Property.Governorate),
            nameof(Property.Area),
            nameof(Property.Block),
            nameof(Property.BuildingNumber),
            nameof(Property.Street),
            nameof(Property.PropertyType),
            nameof(Property.NumberOfUnits),
            nameof(Property.PropertyValue),
            nameof(Property.Status),
            nameof(Property.ManagerId),
        },
        [typeof(Unit)] = new(StringComparer.Ordinal)
        {
            nameof(Unit.UnitNumber),
            nameof(Unit.UnitType),
            nameof(Unit.SquareFeet),
            nameof(Unit.Bathrooms),
            nameof(Unit.Bedrooms),
            nameof(Unit.MonthlyRent),
            nameof(Unit.FloorNumber),
            nameof(Unit.SizeBand),
            // Skip Status — already covered by Unit.ChangeStatus + ReconcileUnitStatusAsync
        },
    };

    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<EntityAuditInterceptor> _logger;

    public EntityAuditInterceptor(
        IHttpContextAccessor? httpContextAccessor,
        ILogger<EntityAuditInterceptor> logger)
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

        // Snapshot the modified entities before iterating; adding AuditLog
        // entries below mutates the change-tracker.
        var modified = ctx.ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Modified && Tracked.ContainsKey(e.Entity.GetType()))
            .ToList();

        foreach (var entry in modified)
        {
            var entityType = entry.Entity.GetType();
            var tracked = Tracked[entityType];
            var diff = BuildDiff(entry, tracked);
            if (diff.Count == 0) continue;

            // PK extraction — every tracked entity has an int <Type>Id property.
            var idProp = entityType.GetProperty(entityType.Name + "Id");
            var id = idProp?.GetValue(entry.Entity) as int? ?? 0;
            if (id == 0) continue;

            ctx.Set<AuditLog>().Add(new AuditLog
            {
                EntityType = entityType.Name,
                EntityId = id,
                Action = "Edited",
                ByUser = string.IsNullOrWhiteSpace(byUser) ? "system" : byUser,
                At = DateTime.UtcNow,
                PayloadJson = JsonSerializer.Serialize(new { changes = diff }, JsonOpts),
            });
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Diff helper — returns <c>{ field: [old, new] }</c> for fields in the
    /// tracked set that actually changed. Internal so tests can poke at it.
    /// </summary>
    internal static Dictionary<string, object?[]> BuildDiff(EntityEntry entry, HashSet<string> tracked)
    {
        var diff = new Dictionary<string, object?[]>();
        foreach (var prop in entry.Properties)
        {
            var name = prop.Metadata.Name;
            if (!tracked.Contains(name)) continue;
            if (!prop.IsModified) continue;

            var oldVal = prop.OriginalValue;
            var newVal = prop.CurrentValue;
            if (Equals(oldVal, newVal)) continue;

            diff[name] = new[] { Normalize(oldVal), Normalize(newVal) };
        }
        return diff;
    }

    private static object? Normalize(object? v) => v switch
    {
        null => null,
        DateTime dt => dt.ToString("yyyy-MM-dd"),
        decimal d => d,
        _ => v,
    };
}
