using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RentCollection.Application.Branding;
using RentCollection.Application.Interfaces;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Infrastructure.Services;

/// <summary>
/// Default implementation — generates a 10-character password,
/// BCrypt-hashes it, emails the plain text to the tenant, and records
/// the action in the audit log. Branding info (company name, login URL)
/// is pulled from <see cref="BrandingSettings"/> so the email looks
/// consistent with the PDFs.
/// </summary>
public class TenantCredentialService : ITenantCredentialService
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailService _email;
    private readonly IAuditLogService _audit;
    private readonly BrandingSettings _brand;
    private readonly IConfiguration _config;
    private readonly ILogger<TenantCredentialService> _logger;

    public TenantCredentialService(
        ApplicationDbContext db,
        IEmailService email,
        IAuditLogService audit,
        IOptions<BrandingSettings> brand,
        IConfiguration config,
        ILogger<TenantCredentialService> logger)
    {
        _db = db;
        _email = email;
        _audit = audit;
        _brand = brand.Value;
        _config = config;
        _logger = logger;
    }

    public async Task<IssueCredentialResult> IssueAsync(
        int tenantId,
        string issueReason,
        string byUser,
        CancellationToken ct = default)
    {
        var tenant = await _db.Tenants
            .IgnoreQueryFilters()  // service caller may be system / job, not a tenant-scoped request
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);

        if (tenant == null)
            return new IssueCredentialResult(false, null, false, "Tenant not found.");

        if (tenant.IsDeleted)
            return new IssueCredentialResult(false, null, false, "Cannot issue credentials to an archived tenant.");

        // 1. Generate + hash a temp password. Format mixes letters and digits
        //    plus one symbol so it satisfies typical complexity rules even
        //    if we later add a server-side policy.
        var plain = GenerateTemporaryPassword();
        tenant.Password = BCrypt.Net.BCrypt.HashPassword(plain);
        tenant.MustChangePasswordOnNextLogin = true;
        tenant.ModifiedDate = DateTime.UtcNow;

        // 2. Audit before email so the record persists even if SMTP times out.
        await _audit.WriteAsync(
            entityType: "Tenant",
            entityId: tenant.TenantId,
            action: "CredentialIssued",
            byUser: byUser,
            payload: new
            {
                reason = issueReason,
                hadEmail = !string.IsNullOrWhiteSpace(tenant.Email),
                // Never log the plain password — only the fact that one was issued.
            },
            ct: ct);

        await _db.SaveChangesAsync(ct);

        // 3. Email the tenant. If they have no email, the admin sees the
        //    password on-screen via the caller's UI fallback.
        if (string.IsNullOrWhiteSpace(tenant.Email))
        {
            _logger.LogInformation(
                "Tenant {TenantId} has no email; credential issued but not sent.", tenant.TenantId);
            return new IssueCredentialResult(true, plain, false,
                "No email on file. Pass the password to the tenant manually.");
        }

        var loginUrl = _config["Branding:TenantPortalUrl"]
            ?? _config["App:TenantPortalUrl"]
            ?? "/TenantPortal";   // relative fallback if no absolute URL set
        var html = BuildHtmlEmail(tenant.FirstName ?? "", tenant.LastName ?? "",
            tenant.GovernmentId ?? "(not set)", plain, loginUrl);
        var subject = $"Your {_brand.CompanyName} tenant portal login";

        var result = await _email.SendAsync(tenant.Email, subject, html, ct);
        if (!result.Success)
        {
            _logger.LogWarning(
                "Failed to email credentials to tenant {TenantId}: {Error}",
                tenant.TenantId, result.ErrorMessage);
            return new IssueCredentialResult(true, plain, false,
                $"Password set, but email failed: {result.ErrorMessage}. Share the password manually.");
        }

        return new IssueCredentialResult(true, plain, true,
            $"Login email sent to {tenant.Email}.");
    }

    /// <summary>
    /// 10-character readable password — avoids ambiguous chars (0/O, l/1)
    /// so tenants can re-type from email without mistakes. Always contains
    /// at least one uppercase, one lowercase, one digit, one symbol.
    /// </summary>
    private static string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghjkmnpqrstuvwxyz";
        const string digit = "23456789";
        const string symbol = "!@#$&*";
        const string all = upper + lower + digit + symbol;

        Span<byte> randomBytes = stackalloc byte[10];
        RandomNumberGenerator.Fill(randomBytes);

        var chars = new char[10];
        chars[0] = upper[randomBytes[0] % upper.Length];
        chars[1] = lower[randomBytes[1] % lower.Length];
        chars[2] = digit[randomBytes[2] % digit.Length];
        chars[3] = symbol[randomBytes[3] % symbol.Length];
        for (int i = 4; i < 10; i++)
        {
            chars[i] = all[randomBytes[i] % all.Length];
        }
        // Shuffle so the required-char positions aren't predictable.
        for (int i = chars.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }

    private string BuildHtmlEmail(string firstName, string lastName,
        string civilId, string tempPassword, string loginUrl)
    {
        var greeting = string.IsNullOrWhiteSpace(firstName + lastName)
            ? "Hello,"
            : $"Dear {firstName} {lastName},".Trim();

        // System.Web isn't referenced; do minimal HTML escape inline so the
        // password (which can contain & < >) renders correctly.
        static string Esc(string s) => s
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        return $@"<!DOCTYPE html>
<html><body style=""font-family:Segoe UI,Tahoma,sans-serif;color:#1e293b;line-height:1.5;max-width:560px;margin:0 auto;padding:24px"">
  <div style=""background:{_brand.SecondaryHex};color:white;padding:18px 24px;border-radius:8px 8px 0 0"">
    <div style=""color:{_brand.PrimaryHex};font-weight:700;font-size:0.75rem;letter-spacing:0.08em;text-transform:uppercase"">{Esc(_brand.CompanyName)}</div>
    <div style=""font-size:1.35rem;font-weight:600;margin-top:4px"">Tenant Portal — your login details</div>
  </div>
  <div style=""background:white;border:1px solid #e2e8f0;border-top:0;border-radius:0 0 8px 8px;padding:24px"">
    <p>{Esc(greeting)}</p>
    <p>An account has been created for you on the <strong>{Esc(_brand.CompanyName)}</strong> tenant portal so you can view invoices, make payments, and request maintenance.</p>
    <div style=""background:#f8fafc;border:1px solid #e2e8f0;border-radius:6px;padding:14px 18px;margin:18px 0"">
      <div style=""font-size:0.75rem;color:#64748b;text-transform:uppercase;letter-spacing:0.05em"">Civil ID (your login)</div>
      <div style=""font-family:Courier New,monospace;font-size:1.05rem;font-weight:600;margin:2px 0 12px"">{Esc(civilId)}</div>
      <div style=""font-size:0.75rem;color:#64748b;text-transform:uppercase;letter-spacing:0.05em"">Temporary password</div>
      <div style=""font-family:Courier New,monospace;font-size:1.05rem;font-weight:700;color:{_brand.PrimaryHex}"">{Esc(tempPassword)}</div>
    </div>
    <p style=""margin:18px 0"">
      <a href=""{Esc(loginUrl)}"" style=""display:inline-block;background:{_brand.PrimaryHex};color:white;padding:11px 22px;border-radius:6px;text-decoration:none;font-weight:600"">Open the tenant portal →</a>
    </p>
    <p style=""background:#fffbeb;border-left:4px solid #f59e0b;padding:10px 14px;font-size:0.9rem"">
      <strong>Important:</strong> you'll be asked to change this password the first time you log in. Pick something only you know.
    </p>
    <p style=""font-size:0.85rem;color:#64748b"">If you didn't expect this email, ignore it — your account stays inactive until you log in.</p>
  </div>
  <div style=""text-align:center;font-size:0.75rem;color:#94a3b8;margin-top:14px"">
    {Esc(_brand.CompanyName)}{(string.IsNullOrEmpty(_brand.ContactAddress) ? "" : $" · {Esc(_brand.ContactAddress)}")}
  </div>
</body></html>";
    }
}
