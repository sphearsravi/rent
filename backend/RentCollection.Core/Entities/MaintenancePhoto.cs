namespace RentCollection.Core.Entities;

/// <summary>
/// Image attached to a maintenance request. Uploaded by either the tenant
/// (Before, capturing the issue) or by an admin/vendor (After, evidence of
/// completion). FilePath is a server-relative path under the upload root —
/// resolved by IDocumentStore at read time so storage can move to S3/Blob
/// later without schema churn.
/// </summary>
public class MaintenancePhoto
{
    public int MaintenancePhotoId { get; set; }
    public int RequestId { get; set; }
    public MaintenanceRequest Request { get; set; } = null!;

    public string FilePath { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public long FileSize { get; set; }
    public string? ContentType { get; set; }
    public string Kind { get; set; } = MaintenancePhotoKind.Before;  // Before/After/Other
    public string? UploadedBy { get; set; }
    public DateTime UploadedDate { get; set; } = DateTime.UtcNow;
}

public static class MaintenancePhotoKind
{
    public const string Before = "Before";
    public const string After = "After";
    public const string Other = "Other";
}
