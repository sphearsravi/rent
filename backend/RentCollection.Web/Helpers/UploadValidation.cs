using Microsoft.AspNetCore.Http;

namespace RentCollection.Web.Helpers;

/// <summary>
/// Shared allow-list for document uploads. The dedicated DocumentsController
/// validates extension + magic bytes; the inline Create-form upload paths
/// (Tenants/Properties/Leases) previously wrote any file to disk unchecked —
/// this closes that hole with a consistent extension + size gate.
/// </summary>
public static class UploadValidation
{
    private static readonly string[] Allowed =
    {
        ".pdf", ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp",
        ".doc", ".docx", ".xls", ".xlsx", ".txt", ".csv"
    };

    private const long MaxBytes = 10 * 1024 * 1024; // 10 MB

    public static bool IsAllowed(IFormFile? file)
    {
        if (file == null || file.Length == 0 || file.Length > MaxBytes) return false;
        var ext = System.IO.Path.GetExtension(file.FileName)?.ToLowerInvariant();
        return !string.IsNullOrEmpty(ext) && System.Array.IndexOf(Allowed, ext) >= 0;
    }
}
