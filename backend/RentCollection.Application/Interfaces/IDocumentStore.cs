namespace RentCollection.Application.Interfaces;

/// <summary>
/// Item 11 — Pluggable storage abstraction. Current implementation is local
/// filesystem; later S3 / Azure Blob can be dropped in without touching
/// callers. Stored paths are opaque keys (`"maintenance/abc.jpg"`) — backend
/// chooses how to resolve them.
/// </summary>
public interface IDocumentStore
{
    Task<StoredDocument> SaveAsync(
        Stream content,
        string folder,
        string originalFileName,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Time-limited URL (or local file path) the client can fetch. For local
    /// storage this is just the relative path under wwwroot. S3/Blob impl
    /// would return a presigned URL.
    /// </summary>
    Task<string> GetReadUrlAsync(string key, TimeSpan validFor, CancellationToken cancellationToken = default);
}

public record StoredDocument(string Key, long SizeBytes, string ContentType);
