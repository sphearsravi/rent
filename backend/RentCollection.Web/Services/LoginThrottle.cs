using Microsoft.Extensions.Caching.Memory;

namespace RentCollection.Web.Services;

/// <summary>
/// SECURITY (audit H2): brute-force / credential-stuffing protection for the
/// login endpoints, which previously had none. Tracks failed attempts per
/// identifier (username / email / Civil ID) in the in-process memory cache and
/// locks the identifier out after too many failures. Keyed by ACCOUNT, not IP,
/// so it works correctly behind the SmarterASP shared-hosting proxy (where many
/// clients share one source IP) and throttles a targeted account regardless of
/// how many IPs the attacker rotates through.
/// </summary>
public interface ILoginThrottle
{
    /// <summary>True if the identifier is currently locked out.</summary>
    bool IsLockedOut(string identifier, out TimeSpan retryAfter);
    /// <summary>Record a failed attempt; starts/extends the lockout window.</summary>
    void RecordFailure(string identifier);
    /// <summary>Clear the counter after a successful login.</summary>
    void Reset(string identifier);
}

public class LoginThrottle : ILoginThrottle
{
    private readonly IMemoryCache _cache;

    // 8 failures within the rolling window trips a lockout for the window length.
    private const int MaxFailures = 8;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    public LoginThrottle(IMemoryCache cache) => _cache = cache;

    private static string Key(string identifier) =>
        "login-fail:" + (identifier ?? string.Empty).Trim().ToLowerInvariant();

    private sealed class Entry
    {
        public int Count;
        public DateTimeOffset ResetAt;
    }

    public bool IsLockedOut(string identifier, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (_cache.TryGetValue(Key(identifier), out Entry? e) && e is not null && e.Count >= MaxFailures)
        {
            retryAfter = e.ResetAt - DateTimeOffset.UtcNow;
            if (retryAfter < TimeSpan.Zero) retryAfter = TimeSpan.Zero;
            return retryAfter > TimeSpan.Zero;
        }
        return false;
    }

    public void RecordFailure(string identifier)
    {
        var key = Key(identifier);
        var e = _cache.TryGetValue(key, out Entry? existing) && existing is not null
            ? existing
            : new Entry { Count = 0, ResetAt = DateTimeOffset.UtcNow.Add(Window) };
        e.Count++;
        e.ResetAt = DateTimeOffset.UtcNow.Add(Window);   // rolling window
        _cache.Set(key, e, Window);
    }

    public void Reset(string identifier) => _cache.Remove(Key(identifier));
}
