namespace RentCollection.Application.Interfaces;

/// <summary>
/// Transport abstraction for email delivery. Implementations should be thin
/// adapters over SendGrid/Mailgun/SMTP. Higher-level concerns (templating,
/// queueing, retries) live in the notification dispatcher above this.
/// </summary>
public interface IEmailService
{
    Task<EmailSendResult> SendAsync(
        string toAddress,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send with optional attachments — used by the monthly executive
    /// dashboard email job (Sprint 4). Implementations that don't support
    /// attachments should fall back to a download-link email body.
    /// </summary>
    Task<EmailSendResult> SendAsync(
        string toAddress,
        string subject,
        string htmlBody,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken cancellationToken = default);
}

public record EmailSendResult(bool Success, string? ErrorMessage = null);

/// <summary>In-memory attachment payload. `MediaType` like "application/pdf".</summary>
public record EmailAttachment(string FileName, byte[] Content, string MediaType);
