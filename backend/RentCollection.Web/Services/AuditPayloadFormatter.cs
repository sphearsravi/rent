using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RentCollection.Web.Services;

/// <summary>
/// Turns an audit-log <c>PayloadJson</c> blob (e.g. {"reason":"Initial
/// onboarding","hadEmail":false}) into human-readable "Label: value" pairs for
/// display — spaced-out labels, Yes/No for booleans, tidy dates — instead of
/// showing raw JSON to the user.
/// </summary>
public static class AuditPayloadFormatter
{
    public static IReadOnlyList<(string Label, string Value)> ToPairs(string? payloadJson)
    {
        var result = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(payloadJson)) return result;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(payloadJson); }
        catch { return result; }   // not valid JSON — caller falls back to raw

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var prop in doc.RootElement.EnumerateObject())
                result.Add((Humanize(prop.Name), FormatValue(prop.Value)));
        }
        return result;
    }

    /// <summary>"hadEmail" / "new_end_date" → "Had Email" / "New End Date".</summary>
    private static string Humanize(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        var sb = new StringBuilder();
        var chars = key.Replace('_', ' ').Replace('-', ' ');
        for (int i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(chars[i - 1]) && chars[i - 1] != ' ')
                sb.Append(' ');
            sb.Append(c);
        }
        var words = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w => char.ToUpper(w[0]) + w.Substring(1)));
    }

    private static string FormatValue(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.True: return "Yes";
            case JsonValueKind.False: return "No";
            case JsonValueKind.Null: return "—";
            case JsonValueKind.Number: return value.ToString();
            case JsonValueKind.String:
                var s = value.GetString() ?? string.Empty;
                // Prettify ISO timestamps; leave other strings as-is.
                if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var dt))
                    return dt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture);
                return s;
            case JsonValueKind.Array:
                return string.Join(", ", value.EnumerateArray().Select(FormatValue));
            case JsonValueKind.Object:
                return string.Join(", ",
                    value.EnumerateObject().Select(p => $"{Humanize(p.Name)}: {FormatValue(p.Value)}"));
            default:
                return value.ToString();
        }
    }
}
