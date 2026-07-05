namespace RentCollection.Core.Entities;

/// <summary>
/// Junction row connecting a Document to one related entity. Lets a single
/// document attach to multiple entities (e.g. a lease addendum linked to the
/// Lease + both Tenants on it).
///
/// EntityType is stored as a string ("Tenant" / "Property" / "Lease" /
/// "MaintenanceRequest") rather than four nullable FK columns because the
/// underlying entities live in different aggregates and would otherwise need
/// four cascade rules — most of which we don't want. The
/// (DocumentId, EntityType, EntityId) tuple is unique so a double-link is
/// impossible.
/// </summary>
public class DocumentLink
{
    public int DocumentLinkId { get; set; }

    public int DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    /// <summary>"Tenant" | "Property" | "Lease" | "MaintenanceRequest". Keep
    /// in sync with <see cref="DocumentLinkEntityTypes"/>.</summary>
    public string EntityType { get; set; } = string.Empty;

    public int EntityId { get; set; }

    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
    public string? LinkedBy { get; set; }
}

public static class DocumentLinkEntityTypes
{
    public const string Tenant = "Tenant";
    public const string Property = "Property";
    public const string Lease = "Lease";
    public const string MaintenanceRequest = "MaintenanceRequest";

    public static readonly string[] All =
        { Tenant, Property, Lease, MaintenanceRequest };
}
