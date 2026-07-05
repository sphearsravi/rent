namespace RentCollection.Application.Interfaces;

/// <summary>
/// Sprint 26 — Firebase Cloud Messaging dispatcher. Implemented in
/// Infrastructure (calls FCM's HTTP API). One method, fits the existing
/// Notification outbox pattern.
///
/// Behaviour when not configured: returns success with a "DryRun" flag
/// in the message so the caller can log but doesn't treat it as a real
/// failure. See <see cref="IEmailService"/> for the same pattern.
/// </summary>
public interface IFcmService
{
    Task<FcmSendResult> SendAsync(
        string deviceToken,
        string title,
        string body,
        IDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of one FCM dispatch. <see cref="TokenInvalid"/> tells the
/// outbox to deactivate the token (don't retry — the device uninstalled
/// or the token expired).
/// </summary>
public sealed record FcmSendResult(
    bool Success,
    string? ErrorMessage = null,
    bool TokenInvalid = false);
