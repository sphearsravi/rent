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
    public string UploadedBy { get; set; } = string.Empty;
    public DateTime UploadedDate { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ApplicationUser? UploadedByUser { get; set; }

    /// <summary>
    /// Many-to-many links to Tenant / Property / Lease / MaintenanceRequest
    /// via the <see cref="DocumentLink"/> junction. Replaces the loose
    /// <see cref="RelatedEntityType"/>/<see cref="RelatedEntityId"/> pair —
    /// those columns remain for a release as a read-fallback while existing
    /// code transitions.
    /// </summary>
    public ICollection<DocumentLink> Links { get; set; } = new List<DocumentLink>();
}
