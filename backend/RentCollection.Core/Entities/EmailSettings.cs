using System.ComponentModel.DataAnnotations;

namespace RentCollection.Core.Entities;

/// <summary>
/// Sprint 25 — singleton row holding the active SMTP configuration the
/// app uses to send all transactional email (invoice notifications,
/// payment receipts, tenant credential emails, etc.).
///
/// Stored in the database (not appsettings.json) so an admin can change
/// providers from the Email Settings page without re-deploying. Only one
/// row should ever exist; <see cref="IsActive"/> guards against accidental
/// duplicates and lets you stage a new config before flipping over.
///
/// Password is stored as plain text — that's a deliberate trade-off:
/// SMTP App Passwords are revocable in seconds at the provider, and any
/// admin with access to this page already has DB access via the connection
/// string. If the threat model changes (multi-tenant SaaS), encrypt with
/// ASP.NET Core DataProtection here.
/// </summary>
public class EmailSettings
{
    public int EmailSettingsId { get; set; }

    // MaxLength annotations match the bootstrap SQL in Program.cs so an
    // EF-managed migration would produce identical column types — no
    // schema drift if someone later runs `dotnet ef migrations add`.

    /// <summary>SMTP server host, e.g. <c>smtp.gmail.com</c>.</summary>
    [MaxLength(255)]
    public string Host { get; set; } = string.Empty;

    /// <summary>SMTP port. 587 (STARTTLS) or 465 (SMTPS) for most providers.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Enable TLS for the connection.</summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>Login username (usually the full email address).</summary>
    [MaxLength(255)]
    public string Username { get; set; } = string.Empty;

    /// <summary>App password or account password. Stored as plain text — see class docs.</summary>
    [MaxLength(500)]
    public string Password { get; set; } = string.Empty;

    /// <summary>The "From" address shown to recipients.</summary>
    [MaxLength(255)]
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>The "From" display name shown to recipients (e.g. <c>ALRTAM Company</c>).</summary>
    [MaxLength(255)]
    public string FromName { get; set; } = "ALRTAM Company";

    /// <summary>
    /// When true, the email service logs the message and returns success
    /// without contacting the SMTP server. Useful for local dev or while
    /// SMTP creds are being provisioned. Defaults to true so the app
    /// doesn't blow up if no creds are configured yet.
    /// </summary>
    public bool DryRun { get; set; } = true;

    /// <summary>Only one row should have IsActive=true at any time.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedAt { get; set; }

    /// <summary>UTC time and outcome of the last test-email attempt — for the admin UI to show.</summary>
    public DateTime? LastTestAt { get; set; }
    [MaxLength(500)]
    public string? LastTestResult { get; set; }   // "OK" or the exception message, truncated to 500
}
