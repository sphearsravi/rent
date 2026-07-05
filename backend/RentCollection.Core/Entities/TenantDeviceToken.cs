using System.ComponentModel.DataAnnotations;

namespace RentCollection.Core.Entities;

/// <summary>
/// Sprint 26 — one row per tenant per installed app instance. The mobile
/// app POSTs its FCM device token after login; the server stores it here
/// and uses it as <see cref="Notification.ToAddress"/> when emitting
/// Push-channel notifications.
///
/// A tenant can have multiple active tokens (phone + tablet, or after a
/// reinstall the old token lingers until FCM expires it). We keep them
/// all <see cref="IsActive"/>=true until FCM returns NotRegistered, then
/// we flip IsActive=false rather than deleting (for audit).
/// </summary>
public class TenantDeviceToken
{
    public int TenantDeviceTokenId { get; set; }

    public int TenantId { get; set; }

    /// <summary>Opaque token from FCM. Length varies by API generation; up to ~250 chars.</summary>
    [MaxLength(500)]
    public string Token { get; set; } = string.Empty;

    /// <summary>"android" / "ios" / "web".</summary>
    [MaxLength(20)]
    public string Platform { get; set; } = string.Empty;

    /// <summary>App-installation-stable ID from the device (Android: ANDROID_ID, iOS: identifierForVendor).
    /// Used to dedupe when the token rotates within the same install.</summary>
    [MaxLength(128)]
    public string? DeviceId { get; set; }

    /// <summary>Locale at registration time — used to format push body text in the
    /// tenant's preferred language if we ever translate notifications.</summary>
    [MaxLength(10)]
    public string? Locale { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenAt { get; set; }
    public DateTime? DeactivatedAt { get; set; }

    [MaxLength(200)]
    public string? DeactivationReason { get; set; }

    public Tenant? Tenant { get; set; }
}
