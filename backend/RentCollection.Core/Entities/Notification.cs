namespace RentCollection.Core.Entities;

/// <summary>
/// A persisted notification queued for delivery. Owned by either a Tenant
/// (sent to their email/app) or a User (admin notifications). The Outbox
/// pattern: writes are committed in the same transaction as the trigger
/// event, then a background worker delivers asynchronously.
/// </summary>
public class Notification
{
    public int NotificationId { get; set; }

    // Recipient — exactly one of these is set.
    public int? TenantId { get; set; }
    public int? UserId { get; set; }

    public string Channel { get; set; } = NotificationChannel.Email;  // Email/SMS/Push/InApp
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? ToAddress { get; set; }                            // resolved email/phone/push-token

    // Outbox lifecycle.
    public string Status { get; set; } = NotificationStatus.Pending;  // Pending/Sent/Failed
    public int Attempts { get; set; } = 0;
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }

    // Optional correlation — what triggered this notification.
    public string? RelatedEntityType { get; set; }   // "Invoice", "Payment", "MaintenanceRequest", "Tenant"
    public int? RelatedEntityId { get; set; }

    public Tenant? Tenant { get; set; }
    public User? User { get; set; }
}

public static class NotificationChannel
{
    public const string Email = "Email";
    public const string Sms = "SMS";
    public const string Push = "Push";
    public const string InApp = "InApp";
}

public static class NotificationStatus
{
    public const string Pending = "Pending";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}
