namespace RentCollection.Infrastructure.Services.Gemini;

/// <summary>
/// Strongly-typed config for the Gemini Vision integration.
/// Bound from configuration section <c>Gemini</c>; the API key MUST be
/// supplied via user-secrets / env-var (<c>Gemini__ApiKey</c>) — NEVER
/// hard-code in appsettings.json. The <see cref="Enabled"/> kill switch
/// makes the controller return 503 immediately so the operator gets a
/// clean error instead of a network timeout when keys rotate or the
/// vendor is down.
/// </summary>
public sealed class GeminiSettings
{
    /// <summary>
    /// Google AI Studio API key. Loaded from <c>Gemini:ApiKey</c> in config,
    /// which in dev is supplied via <c>dotnet user-secrets</c> and in prod
    /// via the <c>Gemini__ApiKey</c> environment variable on the App Pool.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gemini model. <c>gemini-2.5-flash</c> is the cost/speed default;
    /// <c>gemini-2.5-pro</c> for accuracy if flash mis-OCRs Arabic script.
    /// (1.5-flash was deprecated by Google — using it returns 404.)
    /// </summary>
    public string Model { get; set; } = "gemini-2.5-flash";

    /// <summary>
    /// Master kill switch. When false, the enrichment endpoint returns 503.
    /// Use during gateway maintenance or to disable AI features without
    /// rebuilding the app.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Max image size sent to the vendor. Larger images are resized down
    /// before upload — keeps cost predictable and avoids hitting Gemini's
    /// 20 MB per-request limit.
    /// </summary>
    public int MaxImagePixels { get; set; } = 1024;

    /// <summary>Per-session enrichment cap. Defends against runaway scripts.</summary>
    public int RateLimitPerMinute { get; set; } = 10;

    /// <summary>Request timeout in seconds. Gemini's p95 is ~3s; 15s is generous.</summary>
    public int TimeoutSeconds { get; set; } = 15;
}
