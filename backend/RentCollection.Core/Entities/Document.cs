namespace RentCollection.Core.Entities;

public class Document
{
    public int DocumentId { get; set; }
    public string DocumentName { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string? RelatedEntityType { get; set; }
    public int? RelatedEntityId { get; set; }
    public string? Description { get; set; }
    /// <summary>
    /// Free-text audit stamp of who uploaded (the logged-in username, or
    /// "system"). Deliberately NOT a foreign key: the app authenticates via
    /// the custom <c>Users</c> table, not Identity's AspNetUsers, so a FK from
    /// this column to AspNetUsers.Id could never be satisfied and made every
    /// upload fail. The old FK_Documents_AspNetUsers has been dropped.
    /// </summary>
    public string UploadedBy { get; set; } = string.Empty;
    public DateTime UploadedDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Many-to-many links to Tenant / Property / Lease / MaintenanceRequest
    /// via the <see cref="DocumentLink"/> junction. Replaces the loose
    /// <see cref="RelatedEntityType"/>/<see cref="RelatedEntityId"/> pair —
    /// those columns remain for a release as a read-fallback while existing
    /// code transitions.
    /// </summary>
    public ICollection<DocumentLink> Links { get; set; } = new List<DocumentLink>();
}
