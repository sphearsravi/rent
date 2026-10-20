using Microsoft.IdentityModel.Tokens;
using RentCollection.Application.Interfaces;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace RentCollection.Web.Services;

public class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// SECURITY (audit M2): a short, opaque stamp derived from the tenant's
    /// password hash. It is embedded in the JWT and re-checked on every request;
    /// when the password changes the hash (and thus this stamp) changes, so
    /// previously-issued tokens stop validating — i.e. tokens are revoked on
    /// password change. Returns empty for a null/empty hash (no stamp claim).
    /// </summary>
    public static string ComputeSecurityStamp(string? passwordHash)
        => RentCollection.Application.Security.TokenSecurityStamp.Compute(passwordHash);

    /// <param name="longLived">"Remember me" — issue a token valid for
    /// RefreshTokenExpirationDays (default 30) instead of the short
    /// AccessTokenExpirationMinutes, so the mobile app's cold-start refresh
    /// keeps the session alive for the full window. The choice is stamped into
    /// the <c>rm</c> claim so /tenant/refresh can re-issue with the same TTL.</param>
    public string GenerateToken(int tenantId, string email, string? securityStamp = null, bool longLived = false)
    {
        var jwtSettings = _configuration.GetSection("Jwt");
        var secretKey = jwtSettings["Key"];
        if (string.IsNullOrWhiteSpace(secretKey) || secretKey.Length < 32)
            throw new InvalidOperationException("Jwt:Key is not configured. Set via user-secrets or environment variables.");
        var issuer = jwtSettings["Issuer"] ?? "RentCollectionAPI";
        var audience = jwtSettings["Audience"] ?? "RentCollectionClient";
        var expiryMinutes = int.Parse(jwtSettings["AccessTokenExpirationMinutes"] ?? "60");
        var rememberDays = int.Parse(jwtSettings["RefreshTokenExpirationDays"] ?? "30");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, tenantId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("tenantId", tenantId.ToString()),
            new Claim("rm", longLived ? "1" : "0")
        };
        if (!string.IsNullOrEmpty(securityStamp))
            claims.Add(new Claim("stmp", securityStamp));

        var expires = longLived
            ? DateTime.UtcNow.AddDays(rememberDays)
            : DateTime.UtcNow.AddMinutes(expiryMinutes);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
