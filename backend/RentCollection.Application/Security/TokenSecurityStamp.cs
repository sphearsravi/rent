using System.Security.Cryptography;
using System.Text;

namespace RentCollection.Application.Security;

/// <summary>
/// SECURITY (audit M2): computes the JWT revocation "security stamp" — a short,
/// opaque value derived from the account's password hash. Embedded in the token
/// and re-checked per request; a password change alters the hash (and thus this
/// stamp), so previously-issued tokens stop validating. Lives in Application so
/// both the Web token service and Infrastructure services can share one
/// implementation. Returns empty for a null/empty hash.
/// </summary>
public static class TokenSecurityStamp
{
    public static string Compute(string? passwordHash)
    {
        if (string.IsNullOrEmpty(passwordHash)) return string.Empty;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash));
        return Convert.ToHexString(bytes).Substring(0, 16);
    }
}
