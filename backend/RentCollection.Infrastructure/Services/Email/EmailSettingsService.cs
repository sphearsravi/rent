using System.Diagnostics;
using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Infrastructure.Services.Email;

/// <summary>
/// Sprint 25 — DB-backed SMTP configuration. Reads the active singleton
/// row, caches it for 30s, exposes save + test operations.
///
/// Caching: 30s is short enough that a save → next-email-send picks up
/// the new config without an explicit invalidate, but long enough that
/// we don't hit the DB on every notification batch. Save also calls
/// <see cref="InvalidateCache"/> for immediate effect.
/// </summary>
public sealed class EmailSettingsService : IEmailSettingsService
{
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<EmailSettingsService> _logger;
    private const string CacheKey = "email-settings:active";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public EmailSettingsService(
        ApplicationDbContext db,
        IMemoryCache cache,
        ILogger<EmailSettingsService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<EmailSettings> GetActiveAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue<EmailSettings>(CacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var row = await _db.EmailSettings
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderByDescending(s => s.EmailSettingsId)
            .FirstOrDefaultAsync(ct);

        // Falls back to DryRun=true so the rest of the app keeps working
        // even when no SMTP config has been entered yet.
        row ??= new EmailSettings { DryRun = true };

        _cache.Set(CacheKey, row, CacheTtl);
        return row;
    }

    public async Task SaveAsync(EmailSettings settings, CancellationToken ct = default)
    {
        // Sprint 25 — wrap in serializable transaction so two concurrent
        // admin saves can't both leave IsActive=true on different rows
        // (the singleton invariant). Without this, race interleaving
        // could produce a "ghost" config that GetActiveAsync picks
        // non-deterministically once cache TTLs drift between instances.
        await using var tx = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);

        var existing = await _db.EmailSettings.ToListAsync(ct);
        foreach (var e in existing) e.IsActive = false;

        if (settings.EmailSettingsId == 0)
        {
            settings.IsActive = true;
            settings.CreatedAt = DateTime.UtcNow;
            settings.ModifiedAt = DateTime.UtcNow;
            _db.EmailSettings.Add(settings);
        }
        else
        {
            var tracked = existing.FirstOrDefault(s => s.EmailSettingsId == settings.EmailSettingsId);
            if (tracked is null)
            {
                settings.IsActive = true;
                settings.CreatedAt = DateTime.UtcNow;
                _db.EmailSettings.Add(settings);
            }
            else
            {
                tracked.Host = settings.Host;
                tracked.Port = settings.Port;
                tracked.EnableSsl = settings.EnableSsl;
                tracked.Username = settings.Username;
                tracked.Password = settings.Password;
                tracked.FromAddress = settings.FromAddress;
                tracked.FromName = settings.FromName;
                tracked.DryRun = settings.DryRun;
                tracked.IsActive = true;
                tracked.ModifiedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        InvalidateCache();
    }

    public async Task<EmailTestResult> SendTestAsync(
        string toAddress,
        EmailSettings? overrideConfig = null,
        bool stampOutcome = false,
        CancellationToken ct = default)
    {
        var settings = overrideConfig ?? await GetActiveAsync(ct);

        // Auto-stamp when caller asked OR when there's no override (using
        // the saved config directly). Stamping requires a non-zero ID to
        // resolve the row to update.
        var shouldStamp = (stampOutcome || overrideConfig is null) && settings.EmailSettingsId > 0;

        if (string.IsNullOrWhiteSpace(toAddress))
        {
            return new EmailTestResult(false, "Recipient address is empty.", TimeSpan.Zero);
        }

        if (settings.DryRun)
        {
            // Test against DryRun = useless. Tell the operator to flip it off.
            return new EmailTestResult(false,
                "DryRun is enabled — no real SMTP call will be made. Untick DryRun and try again.",
                TimeSpan.Zero);
        }

        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            return new EmailTestResult(false, "SMTP Host is not configured.", TimeSpan.Zero);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new SmtpClient(settings.Host, settings.Port)
            {
                EnableSsl = settings.EnableSsl,
                Credentials = new NetworkCredential(settings.Username, settings.Password),
                Timeout = 15_000, // 15s — fail fast on bad creds rather than hanging the admin page
            };
            using var message = new MailMessage
            {
                From = new MailAddress(settings.FromAddress, settings.FromName),
                Subject = "ALRTAM email settings test",
                Body = "<p>This is a test email from the ALRTAM RentCollection app.</p>" +
                       "<p>If you see this, your SMTP configuration is working.</p>" +
                       $"<p style='color:#64748b;font-size:0.85em'>Sent at {DateTime.UtcNow:u}</p>",
                IsBodyHtml = true,
            };
            message.To.Add(toAddress);

            await client.SendMailAsync(message, ct);
            sw.Stop();

            if (shouldStamp)
            {
                await StampTestOutcomeAsync(settings.EmailSettingsId, ok: true, message: "OK", ct);
            }
            return new EmailTestResult(true, $"Email sent to {toAddress} in {sw.ElapsedMilliseconds} ms.", sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Test email send failed. Host={Host} Port={Port}", settings.Host, settings.Port);
            var truncated = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;

            if (shouldStamp)
            {
                await StampTestOutcomeAsync(settings.EmailSettingsId, ok: false, message: truncated, ct);
            }
            return new EmailTestResult(false, truncated, sw.Elapsed);
        }
    }

    public void InvalidateCache() => _cache.Remove(CacheKey);

    private async Task StampTestOutcomeAsync(int id, bool ok, string message, CancellationToken ct)
    {
        var row = await _db.EmailSettings.FirstOrDefaultAsync(s => s.EmailSettingsId == id, ct);
        if (row is null) return;
        row.LastTestAt = DateTime.UtcNow;
        row.LastTestResult = ok ? "OK" : message;
        await _db.SaveChangesAsync(ct);
        InvalidateCache();
    }
}
