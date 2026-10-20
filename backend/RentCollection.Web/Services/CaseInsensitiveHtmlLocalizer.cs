using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.Extensions.Localization;

namespace RentCollection.Web.Services;

/// <summary>
/// Wraps the default <see cref="IHtmlLocalizer{T}"/> so a lookup that misses
/// retries with other casings of the same key ("Active" ⇄ "active" ⇄ "ACTIVE").
///
/// Why: SharedResource.ar.resx has ~70 key pairs that differ only by case.
/// The compiled .resources store is case-insensitive, so only the FIRST of
/// each pair survives and the other silently falls back to English. Views
/// use both spellings (keys are the English text as typed on the page), so
/// rather than renaming hundreds of call sites we resolve the surviving
/// variant at runtime and cache the mapping per culture.
/// </summary>
public sealed class CaseInsensitiveHtmlLocalizer<TResource> : IHtmlLocalizer<TResource>
{
    private readonly IHtmlLocalizer _inner;
    private static readonly ConcurrentDictionary<string, string?> ResolvedNames = new();

    public CaseInsensitiveHtmlLocalizer(IHtmlLocalizerFactory factory)
    {
        _inner = factory.Create(typeof(TResource));
    }

    public LocalizedHtmlString this[string name]
    {
        get
        {
            var r = _inner[name];
            return r.IsResourceNotFound ? Retry(name, n => _inner[n]) ?? r : r;
        }
    }

    public LocalizedHtmlString this[string name, params object[] arguments]
    {
        get
        {
            var r = _inner[name, arguments];
            return r.IsResourceNotFound ? Retry(name, n => _inner[n, arguments]) ?? r : r;
        }
    }

    public LocalizedString GetString(string name)
    {
        var r = _inner.GetString(name);
        return r.ResourceNotFound ? Retry(name, n => _inner.GetString(n)) ?? r : r;
    }

    public LocalizedString GetString(string name, params object[] arguments)
    {
        var r = _inner.GetString(name, arguments);
        return r.ResourceNotFound ? Retry(name, n => _inner.GetString(n, arguments)) ?? r : r;
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => _inner.GetAllStrings(includeParentCultures);

    // ── helpers ──

    private LocalizedHtmlString? Retry(string name, Func<string, LocalizedHtmlString> lookup)
    {
        var alt = ResolveAlternate(name, n => !lookup(n).IsResourceNotFound);
        return alt == null ? null : lookup(alt);
    }

    private LocalizedString? Retry(string name, Func<string, LocalizedString> lookup)
    {
        var alt = ResolveAlternate(name, n => !lookup(n).ResourceNotFound);
        return alt == null ? null : lookup(alt);
    }

    /// <summary>Returns the casing variant that exists for the current culture, or null.</summary>
    private static string? ResolveAlternate(string name, Func<string, bool> exists)
    {
        var cacheKey = CultureInfo.CurrentUICulture.Name + "\u0001" + name;
        return ResolvedNames.GetOrAdd(cacheKey, _ =>
        {
            foreach (var v in Variants(name))
                if (v != name && exists(v)) return v;
            return null;
        });
    }

    private static IEnumerable<string> Variants(string name)
    {
        if (string.IsNullOrEmpty(name)) yield break;
        yield return name.ToLowerInvariant();
        yield return char.ToUpperInvariant(name[0]) + name[1..].ToLowerInvariant();   // Sentence case
        yield return char.ToUpperInvariant(name[0]) + name[1..];                      // First letter only
        yield return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
        yield return name.ToUpperInvariant();
    }
}
