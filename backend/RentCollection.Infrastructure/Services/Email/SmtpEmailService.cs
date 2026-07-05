using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using RentCollection.Application.Interfaces;

namespace RentCollection.Infrastructure.Services.Email;

/// <summary>
/// Legacy POCO kept only so callers that previously referenced
/// <c>SmtpEmailSettings</c> still compile. The real configuration now
/// lives in the database (see <see cref="EmailSettingsService"/>) and is
/// edited from the Email Settings admin page.
/// </summary>
public class SmtpEmailSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = "no-reply@viijar.local";
    public string FromName { get; set; } = "ALRTAM Company";
    public bool DryRun { get; set; } = true;
}

/// <summary>
/// SMTP-based <see cref="IEmailService"/>. Sprint 25: reads the active
/// config from <see cref="IEmailSettingsService"/> on every send so that
/// changes made on the Email Settings admin page take effect immediately
/// without app restart.
///
/// If the active config has <c>DryRun=true</c> or no Host configured, the
/// message is logged and treated as sent — the rest of the system keeps
/// working before SMTP creds are provisioned.
/// </summary>
public class SmtpEmailService : IEmailService
{
    private readonly IEmailSettingsService _settingsService;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IEmailSettingsService settingsService, ILogger<SmtpEmailService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public Task<EmailSendResult> SendAsync(
        string toAddress, string subject, string htmlBody, CancellationToken cancellationToken = default)
        => SendAsync(toAddress, subject, htmlBody, Array.Empty<EmailAttachment>(), cancellationToken);

    public async Task<EmailSendResult> SendAsync(
        string toAddress,
        string subject,
        string htmlBody,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toAddress))
            return new EmailSendResult(false, "Recipient address is empty");

        var settings = await _settingsService.GetActiveAsync(cancellationToken);

        if (settings.DryRun || string.IsNullOrEmpty(settings.Host))
        {
            _logger.LogInformation(
                "[DRY-RUN email] To={To} Subject={Subject} BodyLength={Length} Attachments={AttachCount}",
                toAddress, subject, htmlBody?.Length ?? 0, attachments.Count);
            return new EmailSendResult(true);
        }

        try
        {
            using var client = new SmtpClient(settings.Host, settings.Port)
            {
                EnableSsl = settings.EnableSsl,
                Credentials = new NetworkCredential(settings.Username, settings.Password),
                // Fail fast (15s) on a bad host/port/TLS handshake instead of blocking
                // the request thread for the 100s default (or longer). Mirrors the
                // test-send path in EmailSettingsService. This is what kept tenant
                // creation "loading forever" when a real SMTP host was configured.
                Timeout = 15_000,
            };

            using var message = new MailMessage
            {
                From = new MailAddress(settings.FromAddress, settings.FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true,
            };
            message.To.Add(toAddress);

            var streams = new List<MemoryStream>();
            try
            {
                foreach (var a in attachments)
                {
                    var ms = new MemoryStream(a.Content);
                    streams.Add(ms);
                    var attachment = new Attachment(ms, a.FileName,
                        System.Net.Mime.MediaTypeNames.Application.Pdf);
                    if (!string.IsNullOrEmpty(a.MediaType))
                    {
                        attachment = new Attachment(ms, a.FileName, a.MediaType);
                    }
                    message.Attachments.Add(attachment);
                }

                await client.SendMailAsync(message, cancellationToken);
                return new EmailSendResult(true);
            }
            finally
            {
                foreach (var s in streams) s.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email send failed. To={To} Subject={Subject}", toAddress, subject);
            return new EmailSendResult(false, ex.Message);
        }
    }
}
