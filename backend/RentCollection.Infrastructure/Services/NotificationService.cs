using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Infrastructure.Services;

/// <summary>
/// Outbox-pattern notification service. Writes [Notification] rows in the same
/// transaction as the triggering business event; a background worker
/// (Hangfire) drains them via [ProcessPendingAsync].
/// </summary>
public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IFcmService _fcmService;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        ApplicationDbContext context,
        IEmailService emailService,
        IFcmService fcmService,
        ILogger<NotificationService> logger)
    {
        _context = context;
        _emailService = emailService;
        _fcmService = fcmService;
        _logger = logger;
    }

    public async Task EnqueueTenantAsync(
        int tenantId,
        string subject,
        string body,
        string channel = NotificationChannel.Email,
        string? relatedEntityType = null,
        int? relatedEntityId = null,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _context.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, cancellationToken);

        if (tenant == null)
        {
            _logger.LogWarning("Notification skipped — tenant {TenantId} not found", tenantId);
            return;
        }

        var toAddress = channel switch
        {
            NotificationChannel.Email => tenant.Email,
            NotificationChannel.Sms => tenant.Phone,
            _ => null
        };

        _context.Set<Notification>().Add(new Notification
        {
            TenantId = tenantId,
            Channel = channel,
            Subject = subject,
            Body = body,
            ToAddress = toAddress,
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = relatedEntityId,
            NextAttemptAt = DateTime.UtcNow,
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task EnqueueUserAsync(
        int userId,
        string subject,
        string body,
        string channel = NotificationChannel.Email,
        string? relatedEntityType = null,
        int? relatedEntityId = null,
        CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .IgnoreQueryFilters()  // bypass company filter — system events target users directly
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);

        if (user == null)
        {
            _logger.LogWarning("Notification skipped — user {UserId} not found", userId);
            return;
        }

        _context.Set<Notification>().Add(new Notification
        {
            UserId = userId,
            Channel = channel,
            Subject = subject,
            Body = body,
            ToAddress = channel == NotificationChannel.Email ? user.Email : user.PhoneNumber,
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = relatedEntityId,
            NextAttemptAt = DateTime.UtcNow,
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ProcessPendingAsync(int maxBatch = 50, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var pending = await _context.Set<Notification>()
            .Where(n => n.Status == NotificationStatus.Pending
                && (n.NextAttemptAt == null || n.NextAttemptAt <= now)
                && n.Attempts < 5)
            .OrderBy(n => n.CreatedAt)
            .Take(maxBatch)
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var note in pending)
        {
            try
            {
                if (string.IsNullOrEmpty(note.ToAddress))
                {
                    note.Status = NotificationStatus.Failed;
                    note.LastError = "Recipient address is empty";
                    continue;
                }

                bool sentOk;
                string? errorMessage;
                bool tokenInvalid = false;

                switch (note.Channel)
                {
                    case NotificationChannel.Email:
                    {
                        var r = await _emailService.SendAsync(
                            note.ToAddress, note.Subject, note.Body, cancellationToken);
                        sentOk = r.Success;
                        errorMessage = r.ErrorMessage;
                        break;
                    }
                    case NotificationChannel.Push:
                    {
                        // Sprint 26 — ToAddress carries the FCM device token. The
                        // outbox writer (EnqueueTenantPushAsync) fanned out one
                        // row per active token so each retries independently.
                        var r = await _fcmService.SendAsync(
                            note.ToAddress, note.Subject, note.Body, data: null, cancellationToken);
                        sentOk = r.Success;
                        errorMessage = r.ErrorMessage;
                        tokenInvalid = r.TokenInvalid;
                        break;
                    }
                    default:
                        sentOk = false;
                        errorMessage = $"Channel {note.Channel} not implemented yet";
                        break;
                }

                note.Attempts += 1;
                if (sentOk)
                {
                    note.Status = NotificationStatus.Sent;
                    note.SentAt = DateTime.UtcNow;
                    note.LastError = null;
                    sent++;
                }
                else if (tokenInvalid)
                {
                    // FCM said "NotRegistered" / "InvalidRegistration" — the
                    // device uninstalled or the token rotated. Don't retry;
                    // mark this notification dead AND deactivate the token
                    // row so future enqueues skip it.
                    note.Status = NotificationStatus.Failed;
                    note.LastError = errorMessage;
                    await DeactivateTokenAsync(note.ToAddress!, errorMessage ?? "TokenInvalid", cancellationToken);
                }
                else
                {
                    // Exponential backoff: 1m, 4m, 16m, 64m, 256m before giving up.
                    note.LastError = errorMessage;
                    note.NextAttemptAt = DateTime.UtcNow.AddMinutes(Math.Pow(4, note.Attempts));
                    if (note.Attempts >= 5)
                        note.Status = NotificationStatus.Failed;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification {Id} dispatch errored", note.NotificationId);
                note.Attempts += 1;
                note.LastError = ex.Message;
                if (note.Attempts >= 5) note.Status = NotificationStatus.Failed;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return sent;
    }

    public async Task<int> EnqueueTenantPushAsync(
        int tenantId,
        string title,
        string body,
        IDictionary<string, string>? data = null,
        string? relatedEntityType = null,
        int? relatedEntityId = null,
        CancellationToken cancellationToken = default)
    {
        // Sprint 26 — find every active FCM token for the tenant and create
        // one Notification per token. The dispatcher's per-row retry +
        // token-deactivation flow then handles each device independently.
        var tokens = await _context.TenantDeviceTokens
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.IsActive && !string.IsNullOrEmpty(t.Token))
            .Select(t => t.Token)
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0)
        {
            _logger.LogDebug(
                "Push skipped — tenant {TenantId} has no active device tokens",
                tenantId);
            return 0;
        }

        foreach (var token in tokens)
        {
            _context.Set<Notification>().Add(new Notification
            {
                TenantId = tenantId,
                Channel = NotificationChannel.Push,
                Subject = title,                // FCM "notification.title"
                Body = body,                    // FCM "notification.body"
                ToAddress = token,              // FCM "to" — the device token
                RelatedEntityType = relatedEntityType,
                RelatedEntityId = relatedEntityId,
                NextAttemptAt = DateTime.UtcNow,
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
        return tokens.Count;
    }

    private async Task DeactivateTokenAsync(string token, string reason, CancellationToken ct)
    {
        var row = await _context.TenantDeviceTokens
            .FirstOrDefaultAsync(t => t.Token == token && t.IsActive, ct);
        if (row is null) return;

        row.IsActive = false;
        row.DeactivatedAt = DateTime.UtcNow;
        row.DeactivationReason = reason.Length > 200 ? reason[..200] : reason;
        // SaveChangesAsync is called by the dispatcher after this — no separate save here.
    }
}
