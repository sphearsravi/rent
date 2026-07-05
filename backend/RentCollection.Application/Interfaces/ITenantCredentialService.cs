namespace RentCollection.Application.Interfaces;

/// <summary>
/// Issues a fresh temporary password to a Tenant and emails it to them.
/// Used at two moments:
///   • Initial onboarding — fires automatically when TenantsController.Create
///     succeeds with a non-empty Email.
///   • Admin "Resend login" — fires from the Tenants index kebab when a
///     tenant has lost or never received their first email.
///
/// Implementations must:
///   1. Generate a strong-but-readable random password (10 chars, mixed)
///   2. BCrypt-hash and store it on Tenant.Password
///   3. Set Tenant.MustChangePasswordOnNextLogin = true
///   4. Send an email to the tenant with the plain-text password +
///      login URL + a "must change on first login" warning
///   5. Write an AuditLog row (entityType "Tenant", action "CredentialIssued")
///
/// Returns the issued plain-text password too — the admin form can fall
/// back to showing it on-screen when email delivery fails or the tenant
/// has no email on file.
/// </summary>
public interface ITenantCredentialService
{
    Task<IssueCredentialResult> IssueAsync(
        int tenantId,
        string issueReason,
        string byUser,
        CancellationToken ct = default);
}

public record IssueCredentialResult(
    bool Success,
    string? GeneratedPassword,   // null when Success=false; never logged
    bool EmailSent,
    string? Message);
