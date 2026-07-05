namespace RentCollection.Core.Entities;

/// <summary>
/// Immutable record of a material domain action — who did what, when, to which
/// entity, and (for actions with parameters) the payload that drove it.
///
/// Introduced for LEASE_AUDIT Flaw #16 (no audit trail for Renew / Terminate /
/// Transfer). Designed as a polymorphic log so other domains (Tenants Forget,
/// Properties ToggleStatus, etc.) can write here too without per-domain tables.
///
/// Writes go through <c>IAuditLogService</c>; reads will be exposed via a
/// dedicated <c>/Audit/{entityType}/{entityId}</c> view in a later sprint.
/// </summary>
public class AuditLog
{
    public long AuditLogId { get; set; }

    /// <summary>"Lease" / "Tenant" / "Property" / etc.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>The integer primary key of the affected entity.</summary>
    public int EntityId { get; set; }

    /// <summary>Domain verb — "Renewed", "Terminated", "Transferred", "Forgot", …</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>User name from <c>User.Identity?.Name</c>; "system" for jobs.</summary>
    public string ByUser { get; set; } = string.Empty;

    /// <summary>UTC instant the action ran.</summary>
    public DateTime At { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// JSON-serialised parameters of the action (newEndDate, newMonthlyRent,
    /// transitionFrom/to, etc.). Optional — Forget actions carry no payload.
    /// </summary>
    public string? PayloadJson { get; set; }
}
