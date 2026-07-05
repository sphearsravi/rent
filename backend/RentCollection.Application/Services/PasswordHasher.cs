using System.Security.Cryptography;
using System.Text;

namespace RentCollection.Application.Services;

public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string passwordHash);
}

public class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16; // 128 bits
    private const int KeySize = 32; // 256 bits
    // OWASP 2023 PBKDF2-SHA256 recommendation. Old hashes encoded with fewer
    // iterations still verify because the iteration count is embedded in the
    // stored value (see Verify below).
    private const int CurrentIterations = 600_000;
    private const int LegacyIterations = 10_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            CurrentIterations,
            Algorithm,
            KeySize);

        // Versioned format: v2:<iters>:<saltB64>:<hashB64>
        return $"v2:{CurrentIterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        if (string.IsNullOrEmpty(passwordHash))
            return false;

        var parts = passwordHash.Split(':');

        // New format: v2:<iters>:<saltB64>:<hashB64>
        if (parts.Length == 4 && parts[0] == "v2" && int.TryParse(parts[1], out var iters))
        {
            var salt = Convert.FromBase64String(parts[2]);
            var hash = Convert.FromBase64String(parts[3]);
            var inputHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, iters, Algorithm, KeySize);
            return CryptographicOperations.FixedTimeEquals(inputHash, hash);
        }

        // Legacy format: <saltB64>:<hashB64>, 10k iterations.
        if (parts.Length == 2)
        {
            var salt = Convert.FromBase64String(parts[0]);
            var hash = Convert.FromBase64String(parts[1]);
            var inputHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, LegacyIterations, Algorithm, KeySize);
            return CryptographicOperations.FixedTimeEquals(inputHash, hash);
        }

        return false;
    }
}
