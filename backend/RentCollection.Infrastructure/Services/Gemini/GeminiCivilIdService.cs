using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RentCollection.Application.Interfaces;

namespace RentCollection.Infrastructure.Services.Gemini;

/// <summary>
/// Gemini Vision implementation of <see cref="ICivilIdEnrichmentService"/>.
/// Sends a Civil ID card image (JPEG / PNG / PDF) to Gemini with a strict
/// JSON-only response schema, parses the structured output, and surfaces it
/// for operator confirmation before any DB write.
///
/// Logging is deliberately minimal: we log latency, success/failure, image
/// size, and Gemini's HTTP status — but NEVER the prompt body, image bytes,
/// or the parsed response. PII never reaches the log sink.
/// </summary>
public sealed class GeminiCivilIdService : ICivilIdEnrichmentService
{
    private readonly HttpClient _http;
    private readonly GeminiSettings _settings;
    private readonly ILogger<GeminiCivilIdService> _logger;

    // Pass the API key via the `x-goog-api-key` header instead of as a query
    // string — .NET's default HttpClient logger logs request URLs at
    // Information level, which would write the key to the log sink with
    // every call. Header-based auth keeps the secret out of the log trail.
    private const string GeminiEndpoint =
        "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";

    /// <summary>
    /// Strict JSON-only prompt. Gemini supports a `response_mime_type` of
    /// `application/json` which constrains the output, but we also state
    /// it in the prompt so older models honour the contract.
    /// </summary>
    private const string Prompt = @"You are extracting structured fields from a Kuwait Civil ID card (PACI).
Return ONLY a JSON object with these exact keys; use null when a field isn't legible:
{
  ""civilId"": ""12-digit Civil ID number, digits only"",
  ""fullNameArabic"": ""full name in Arabic script as it appears on the card"",
  ""fullNameEnglish"": ""full name in English/Latin script as it appears on the card"",
  ""dateOfBirth"": ""YYYY-MM-DD format"",
  ""expiryDate"": ""YYYY-MM-DD format"",
  ""gender"": ""Male or Female"",
  ""nationality"": ""nationality string as printed on the card""
}
Do NOT include any prose, markdown, or backticks — JSON object only.";

    public GeminiCivilIdService(
        HttpClient http,
        IOptions<GeminiSettings> settings,
        ILogger<GeminiCivilIdService> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
    }

    public async Task<CivilIdEnrichmentResult> EnrichFromImageAsync(
        byte[] imageBytes, string mimeType, CancellationToken ct = default)
    {
        if (!_settings.Enabled)
            return Fail("Civil ID enrichment is disabled.");
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            return Fail("Civil ID enrichment is not configured (missing API key).");
        if (imageBytes.Length == 0)
            return Fail("Image payload is empty.");

        var url = string.Format(GeminiEndpoint, _settings.Model);
        var base64 = Convert.ToBase64String(imageBytes);

        var request = new GeminiRequest
        {
            Contents = new[]
            {
                new GeminiContent
                {
                    Parts = new GeminiPart[]
                    {
                        new() { Text = Prompt },
                        new() { InlineData = new GeminiInlineData { MimeType = mimeType, Data = base64 } },
                    }
                }
            },
            GenerationConfig = new GeminiGenerationConfig
            {
                ResponseMimeType = "application/json",
                Temperature = 0.1, // low — we want determinism, not creativity
                // 2.5 series produces "thinking" tokens before the response.
                // 512 was getting eaten by thinking → truncated JSON →
                // deserializer threw. 4096 leaves comfortable margin.
                MaxOutputTokens = 4096,
                // Disable internal thinking for this strict-schema task. We
                // already give the model a tight JSON schema to fill — there's
                // nothing to "think" about, just extract.
                ThinkingConfig = new GeminiThinkingConfig { ThinkingBudget = 0 },
            },
        };

        var started = DateTime.UtcNow;
        try
        {
            // Build the HttpRequestMessage manually so we can attach the
            // x-goog-api-key header without exposing the key in the URL.
            using var msg = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(request, options: JsonOpts),
            };
            msg.Headers.TryAddWithoutValidation("x-goog-api-key", _settings.ApiKey);

            var resp = await _http.SendAsync(msg, ct);
            var elapsed = (DateTime.UtcNow - started).TotalMilliseconds;

            // NOTE: log latency + status only. Prompt + response bodies are PII.
            _logger.LogInformation(
                "Gemini enrich call: status={Status} elapsedMs={Elapsed:0} imageBytes={Bytes}",
                (int)resp.StatusCode, elapsed, imageBytes.Length);

            if (!resp.IsSuccessStatusCode)
            {
                // Surface Google's error message so the operator sees a
                // useful hint (e.g. "model not found", "quota exceeded",
                // "image too small"). Keep it short — full body is PII risk.
                string detail = "";
                try
                {
                    var errBody = await resp.Content.ReadAsStringAsync(ct);
                    // Truncate hard so a verbose error doesn't bloat the log/UI.
                    detail = errBody.Length > 200 ? errBody.Substring(0, 200) + "…" : errBody;
                }
                catch { /* swallow — better to surface status than NRE here */ }
                return Fail($"Vision service returned {(int)resp.StatusCode}: {detail}");
            }

            var body = await resp.Content.ReadFromJsonAsync<GeminiResponse>(JsonOpts, ct);
            var raw = body?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
            if (string.IsNullOrWhiteSpace(raw))
                return Fail("Vision service returned an empty result.");

            // Gemini may wrap the JSON in markdown fences even with the
            // application/json hint — strip them defensively.
            var json = StripMarkdownFence(raw);

            var parsed = JsonSerializer.Deserialize<CivilIdRawResponse>(json, JsonOpts);
            if (parsed == null)
                return Fail("Vision service result couldn't be parsed.");

            return new CivilIdEnrichmentResult(
                Success: true,
                CivilId: SanitiseDigits(parsed.CivilId),
                FullNameArabic: parsed.FullNameArabic?.Trim(),
                FullNameEnglish: parsed.FullNameEnglish?.Trim(),
                DateOfBirth: ParseDate(parsed.DateOfBirth),
                ExpiryDate: ParseDate(parsed.ExpiryDate),
                Gender: parsed.Gender?.Trim(),
                Nationality: parsed.Nationality?.Trim(),
                ErrorMessage: null);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Gemini enrich call timed out after {Sec}s.", _settings.TimeoutSeconds);
            return Fail("Vision service timed out — please try again or enter the fields manually.");
        }
        catch (JsonException jx)
        {
            // Most common cause is `maxOutputTokens` getting consumed by the
            // model's internal thinking before any JSON is emitted →
            // truncated body. Surface this as a config hint, not a black-box
            // failure.
            _logger.LogError(
                "Gemini response wasn't valid JSON — likely truncated: {Message}",
                jx.Message);
            return Fail("Vision service returned a truncated response. Try a clearer image, or contact admin (token limit may need tuning).");
        }
        catch (Exception ex)
        {
            // Log type + message only; never the prompt or response.
            _logger.LogError("Gemini enrich call failed: {Type} {Message}",
                ex.GetType().Name, ex.Message);
            return Fail("Vision service failed — please enter the fields manually.");
        }
    }

    // ---------- helpers ----------

    private static CivilIdEnrichmentResult Fail(string msg) =>
        new(false, null, null, null, null, null, null, null, msg);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTime.TryParse(raw, out var d) ? d.Date : null;
    }

    private static string? SanitiseDigits(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length > 0 ? digits : null;
    }

    private static string StripMarkdownFence(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith("```"))
        {
            var firstNewline = raw.IndexOf('\n');
            if (firstNewline >= 0) raw = raw[(firstNewline + 1)..];
            if (raw.EndsWith("```")) raw = raw[..^3];
        }
        return raw.Trim();
    }

    // ---------- Gemini wire shapes ----------
    private sealed class GeminiRequest
    {
        public GeminiContent[] Contents { get; set; } = Array.Empty<GeminiContent>();
        public GeminiGenerationConfig? GenerationConfig { get; set; }
    }
    private sealed class GeminiContent
    {
        public GeminiPart[] Parts { get; set; } = Array.Empty<GeminiPart>();
    }
    private sealed class GeminiPart
    {
        public string? Text { get; set; }
        [JsonPropertyName("inline_data")]
        public GeminiInlineData? InlineData { get; set; }
    }
    private sealed class GeminiInlineData
    {
        [JsonPropertyName("mime_type")]
        public string MimeType { get; set; } = "image/jpeg";
        public string Data { get; set; } = "";
    }
    private sealed class GeminiGenerationConfig
    {
        [JsonPropertyName("response_mime_type")]
        public string? ResponseMimeType { get; set; }
        public double? Temperature { get; set; }
        [JsonPropertyName("maxOutputTokens")]
        public int? MaxOutputTokens { get; set; }
        [JsonPropertyName("thinkingConfig")]
        public GeminiThinkingConfig? ThinkingConfig { get; set; }
    }
    private sealed class GeminiThinkingConfig
    {
        [JsonPropertyName("thinkingBudget")]
        public int ThinkingBudget { get; set; }
    }
    private sealed class GeminiResponse
    {
        public GeminiCandidate[]? Candidates { get; set; }
    }
    private sealed class GeminiCandidate
    {
        public GeminiContent? Content { get; set; }
    }
    private sealed class CivilIdRawResponse
    {
        public string? CivilId { get; set; }
        public string? FullNameArabic { get; set; }
        public string? FullNameEnglish { get; set; }
        public string? DateOfBirth { get; set; }
        public string? ExpiryDate { get; set; }
        public string? Gender { get; set; }
        public string? Nationality { get; set; }
    }
}
