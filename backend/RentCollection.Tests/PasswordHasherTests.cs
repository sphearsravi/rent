using FluentAssertions;
using RentCollection.Application.Services;

namespace RentCollection.Tests;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Hash_then_verify_round_trips()
    {
        var hash = _hasher.HashPassword("correct horse battery staple");
        _hasher.VerifyPassword("correct horse battery staple", hash).Should().BeTrue();
    }

    [Fact]
    public void Wrong_password_does_not_verify()
    {
        var hash = _hasher.HashPassword("abc");
        _hasher.VerifyPassword("xyz", hash).Should().BeFalse();
    }

    [Fact]
    public void New_hashes_use_v2_format_with_600k_iterations()
    {
        var hash = _hasher.HashPassword("anything");
        var parts = hash.Split(':');
        parts.Should().HaveCount(4);
        parts[0].Should().Be("v2");
        parts[1].Should().Be("600000");
    }

    [Fact]
    public void Legacy_format_still_verifies_for_backward_compatibility()
    {
        // Produce a legacy-format hash directly so old DB rows continue to log in.
        // Salt + key bytes don't need to be meaningful — we just need the format
        // to match what the previous hasher emitted ("<salt>:<hash>", 10k iters).
        var salt = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        var legacyHash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            System.Text.Encoding.UTF8.GetBytes("legacypassword"),
            salt,
            10_000,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            32);

        var stored = $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(legacyHash)}";
        _hasher.VerifyPassword("legacypassword", stored).Should().BeTrue();
        _hasher.VerifyPassword("wrong", stored).Should().BeFalse();
    }
}
