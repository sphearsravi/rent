namespace RentCollection.Application.Interfaces;

/// <summary>
/// Service for generating JWT tokens for tenant authentication
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Generates a JWT token for the specified tenant
    /// </summary>
    string GenerateToken(int tenantId, string email, string? securityStamp = null, bool longLived = false);
}
