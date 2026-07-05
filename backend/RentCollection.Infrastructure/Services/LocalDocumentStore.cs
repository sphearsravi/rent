using Microsoft.AspNetCore.Hosting;
using RentCollection.Application.Interfaces;

namespace RentCollection.Infrastructure.Services;

/// <summary>
/// Local-filesystem [IDocumentStore] implementation. Uses wwwroot/uploads as
/// the storage root. Key format: `<folder>/<guid><ext>` — opaque to callers,
/// resolved here. Path traversal is rejected.
/// </summary>
public class LocalDocumentStore : IDocumentStore
{
    private readonly string _root;

    public LocalDocumentStore(IWebHostEnvironment env)
    {
        _root = Path.GetFullPath(Path.Combine(env.WebRootPath, "uploads"));
        Directory.CreateDirectory(_root);
    }

    public async Task<StoredDocument> SaveAsync(
        Stream content,
        string folder,
        string originalFileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folder)) folder = "misc";
        var safeFolder = folder.Replace("..", "").TrimStart('/', '\\');
        var ext = Path.GetExtension(originalFileName).ToLowerInvariant();
        var key = $"{safeFolder.Replace('\\', '/')}/{Guid.NewGuid():N}{ext}";
        var fullPath = Resolve(key);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
        {
            await content.CopyToAsync(fs, cancellationToken);
        }
        var size = new FileInfo(fullPath).Length;
        return new StoredDocument(key, size, contentType);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullPath = Resolve(key);
        if (!File.Exists(fullPath)) return Task.FromResult<Stream?>(null);
        Stream s = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult<Stream?>(s);
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullPath = Resolve(key);
        if (!File.Exists(fullPath)) return Task.FromResult(false);
        File.Delete(fullPath);
        return Task.FromResult(true);
    }

    public Task<string> GetReadUrlAsync(string key, TimeSpan validFor, CancellationToken cancellationToken = default)
    {
        // Local storage: just expose the relative URL. S3/Blob impl would
        // mint a presigned URL with the validFor expiry baked in.
        return Task.FromResult($"/uploads/{key}");
    }

    private string Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key is required", nameof(key));
        var normalized = key.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized)) throw new UnauthorizedAccessException("Absolute keys not allowed");
        var combined = Path.GetFullPath(Path.Combine(_root, normalized));
        var rootWithSep = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path escapes storage root");
        return combined;
    }
}
