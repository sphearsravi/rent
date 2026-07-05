namespace RentCollection.Core.Entities;

/// <summary>
/// Item 10 — Marker for entities that participate in soft-delete. Deleting a
/// row sets `IsDeleted = true` + stamps DeletedAt; the EF query filter hides
/// soft-deleted rows from regular reads. Use `IgnoreQueryFilters()` to see
/// them (e.g. for GDPR export or audit).
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
    string? DeletedBy { get; set; }
}
