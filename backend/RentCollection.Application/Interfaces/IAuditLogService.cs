namespace RentCollection.Application.Interfaces;

/// <summary>
/// Polymorphic audit-log writer. Domain actions (Renew / Terminate / Transfer
/// / Forget / Restore / …) call this from inside the same transaction as
/// the action so the log is consistent with the data change.
///
/// Reads are deliberately not on this interface — they belong on a future
/// query service or controller. Keeping this write-only lets jobs and tests
/// register a no-op implementation when audit isn't relevant.
///
/// Introduced for LEASE_AUDIT Flaw #16.
/// </summary>
public interface IAuditLogService
{
    /// <summary>
    /// Record one audit event. <paramref name="payload"/> is JSON-serialised
    /// and stored on the row; pass null when the action has no parameters.
    /// Implementations must not throw on serialisation problems — fall back
    /// to <c>"{}"</c> and let the caller's transaction succeed.
    /// </summary>
    Task WriteAsync(
        string entityType,
        int entityId,
        string action,
        string byUser,
        object? payload = null,
        CancellationToken ct = default);
}
