namespace RentCollection.Application.Services;

/// <summary>
/// Parses Kuwait Civil ID numbers (PACI) into their constituent pieces.
///
/// A Civil ID is 12 digits structured as:
///   C YY MM DD NNNNN X
///     │ │  │  │  │     └─ checksum digit
///     │ │  │  │  └─ 5-digit sequential
///     │ │  │  └─ day of birth
///     │ │  └─ month of birth
///     │ └─ last two digits of year
///     └─ century indicator (2 = 1900s, 3 = 2000s)
///
/// We DO validate format + check the date is real (catches transposition
/// errors at entry time). The checksum is a soft warning rather than a
/// hard reject — operator-typed IDs do collide with edge-case checksum
/// rules in the wild, and the field is treated as a hint until the actual
/// Civil ID card is captured (Phase 2 OCR path).
/// </summary>
public static class CivilIdParser
{
    /// <summary>PACI weights for digits 1-11 of the Civil ID number.</summary>
    private static readonly int[] ChecksumWeights = { 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2 };

    public sealed record ParseResult(
        bool IsFormatValid,
        bool IsChecksumValid,
        DateTime? DateOfBirth,
        string? Gender,
        string? Error)
    {
        /// <summary>True iff both format AND checksum check out.</summary>
        public bool IsFullyValid => IsFormatValid && IsChecksumValid;
    }

    /// <summary>
    /// Parse a Civil ID string. Whitespace is trimmed; non-digit characters
    /// are NOT — callers should sanitise upstream (the format check below
    /// catches non-digits as "wrong length / non-numeric").
    /// </summary>
    public static ParseResult Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new(false, false, null, null, "Civil ID is required.");

        var id = input.Trim();
        if (id.Length != 12)
            return new(false, false, null, null,
                $"Civil ID must be exactly 12 digits (got {id.Length}).");

        if (!id.All(char.IsDigit))
            return new(false, false, null, null, "Civil ID must contain only digits.");

        var digits = id.Select(c => c - '0').ToArray();

        // Century: 2 = 19xx, 3 = 20xx. Anything else is malformed.
        var centuryIndicator = digits[0];
        if (centuryIndicator != 2 && centuryIndicator != 3)
            return new(false, false, null, null,
                "Civil ID must start with 2 (1900s) or 3 (2000s).");
        var century = centuryIndicator == 2 ? 1900 : 2000;

        var year = century + digits[1] * 10 + digits[2];
        var month = digits[3] * 10 + digits[4];
        var day = digits[5] * 10 + digits[6];

        if (month < 1 || month > 12)
            return new(false, false, null, null, $"Civil ID month '{month:00}' is not valid.");
        if (day < 1 || day > 31)
            return new(false, false, null, null, $"Civil ID day '{day:00}' is not valid.");

        DateTime dob;
        try
        {
            dob = new DateTime(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            // e.g. Feb 30 — passes month/day range checks individually but
            // isn't a real calendar date.
            return new(false, false, null, null,
                $"Civil ID date {year}-{month:00}-{day:00} is not a real calendar date.");
        }

        if (dob > DateTime.UtcNow.Date)
            return new(false, false, null, null,
                $"Civil ID date of birth ({dob:yyyy-MM-dd}) is in the future.");

        // Gender hint — the standard convention is that the sequential block
        // is odd for males / even for females. Not 100% reliable in the wild,
        // so we surface it as a hint, never as a hard fact.
        var sequential = digits[7] * 10_000 + digits[8] * 1000 + digits[9] * 100 + digits[10] * 10;
        var gender = sequential % 2 == 1 ? "Male" : "Female";

        // Checksum: sum of (digit * weight) for the first 11 digits,
        // mod 11, must equal the 12th digit. Some PACI variants treat
        // result=10 as 0; we accept either to be permissive.
        var weightedSum = 0;
        for (int i = 0; i < 11; i++)
            weightedSum += digits[i] * ChecksumWeights[i];
        var mod = weightedSum % 11;
        var checksumValid = mod == digits[11]
            || (mod == 10 && digits[11] == 0);

        return new(
            IsFormatValid: true,
            IsChecksumValid: checksumValid,
            DateOfBirth: dob,
            Gender: gender,
            Error: checksumValid ? null : "Checksum digit doesn't match — double-check the number.");
    }
}
