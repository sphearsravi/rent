using OtpNet;

namespace RentCollection.Web.Services;

/// <summary>
/// Item 12 — Thin wrapper over OtpNet for TOTP MFA. Generates a secret on
/// enrolment, validates 6-digit codes from authenticator apps (Google
/// Authenticator, Authy, 1Password, etc.).
/// </summary>
public class TotpService
{
    public string GenerateSecret()
    {
        var bytes = KeyGeneration.GenerateRandomKey(20);
        return Base32Encoding.ToString(bytes);
    }

    /// <summary>otpauth:// URI consumed by authenticator app QR codes.</summary>
    public string BuildOtpAuthUri(string secret, string accountName, string issuer = "VIIJAR")
    {
        var encodedIssuer = Uri.EscapeDataString(issuer);
        var encodedAccount = Uri.EscapeDataString(accountName);
        return $"otpauth://totp/{encodedIssuer}:{encodedAccount}?secret={secret}&issuer={encodedIssuer}&algorithm=SHA1&digits=6&period=30";
    }

    public bool Verify(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code)) return false;
        try
        {
            var keyBytes = Base32Encoding.ToBytes(secret);
            var totp = new Totp(keyBytes);
            // Allow one step (30s) of clock drift either side.
            return totp.VerifyTotp(code.Trim(), out _, new VerificationWindow(previous: 1, future: 1));
        }
        catch
        {
            return false;
        }
    }
}
