using RentCollection.Core.Entities;

namespace RentCollection.Application.Interfaces;

/// <summary>
/// Sprint 25 — DB-backed SMTP configuration. Replaces the static
/// <c>IOptions&lt;SmtpEmailSettings&gt;</c> binding so an admin can change
/// the email provider via the Email Settings page without re-deploying.
///
/// The implementation caches the active row for ~30 seconds; calling
/// <see cref="InvalidateCache"/> after a write makes the new config
/// active immediately.
/// </summary>
public interface IEmailSettingsService
{
    /// <summary>Get the active SMTP configuration. Never returns null —
    /// falls back to safe defaults (DryRun=true) if no row is configured.</summary>
    Task<EmailSettings> GetActiveAsync(CancellationToken ct = default);

    /// <summary>Upsert the singleton active row. Marks all other rows IsActive=false.</summary>
    Task SaveAsync(EmailSettings settings, CancellationToken ct = default);

    /// <summary>Send a one-off test email using either the current active config
    /// or the supplied <paramref name="overrideConfig"/> (so the admin can
    /// test new values before saving). When <paramref name="stampOutcome"/>
    /// is true AND the override has a non-zero EmailSettingsId, the test
    /// result is persisted on that row for display in the admin UI.</summary>
    Task<EmailTestResult> SendTestAsync(
        string toAddress,
        EmailSettings? overrideConfig = null,
        bool stampOutcome = false,
        CancellationToken ct = default);

    /// <summary>Drop the in-memory cache so the next read hits the DB.</summary>
    void InvalidateCache();
}

public sealed record EmailTestResult(bool Success, string Message, TimeSpan Elapsed);
