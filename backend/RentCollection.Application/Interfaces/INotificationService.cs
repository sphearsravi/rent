namespace RentCollection.Application.Interfaces;

/// <summary>
/// Enqueues notifications onto the outbox. The actual delivery happens in a
/// background worker (Hangfire job) that drains the outbox and calls
/// [IEmailService] / [ISmsService] / etc.
///
/// `EnqueueTenant` resolves the tenant's email/phone at enqueue time so the
/// outbox row is self-contained even if the tenant record changes later.
/// </summary>
public interface INotificationService
{
    Task EnqueueTenantAsync(
        int tenantId,
        string subject,
        string body,
        string channel = "Email",
        string? relatedEntityType = null,
        int? relatedEntityId = null,
        CancellationToken cancellationToken = default);

    Task EnqueueUserAsync(
        int userId,
        string subject,
        string body,
        string channel = "Email",
        string? relatedEntityType = null,
        int? relatedEntityId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sprint 26 — fan-out a push notification across every active FCM
    /// device token the tenant has registered. One Notification row per
    /// token so the existing outbox dispatcher handles each independently
    /// (per-device retry, deactivation on TokenInvalid).
    /// </summary>
    Task<int> EnqueueTenantPushAsync(
        int tenantId,
        string title,
        string body,
        IDictionary<string, string>? data = null,
        string? relatedEntityType = null,
        int? relatedEntityId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drain pending notifications and dispatch via the configured transport.
    /// Called by a Hangfire job on a schedule.
    /// </summary>
    Task<int> ProcessPendingAsync(int maxBatch = 50, CancellationToken cancellationToken = default);
}
