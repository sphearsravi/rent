namespace RentCollection.Web.Services;

/// <summary>
/// SECURITY (audit M3): one shared password-strength rule for every set-password
/// path (admin create/change, tenant change-password), replacing the mix of
/// "min 6", "min 8", and "no policy at all". Returns null when the password is
/// acceptable, or a human-readable error otherwise.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;

    public static string? Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinLength)
            return $"Password must be at least {MinLength} characters.";
        if (!password.Any(char.IsLetter))
            return "Password must contain at least one letter.";
        if (!password.Any(char.IsDigit))
            return "Password must contain at least one digit.";
        return null;
    }
}
