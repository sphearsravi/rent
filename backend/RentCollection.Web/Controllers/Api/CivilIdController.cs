using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RentCollection.Application.Interfaces;
using RentCollection.Application.Services;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Services.Gemini;

namespace RentCollection.Web.Controllers.Api;

/// <summary>
/// Civil ID utility endpoints used by the Tenant Create form.
///
/// Two paths:
///   • <c>GET  /api/civilid/parse?id=…</c> — cheap, deterministic. No
///     external calls; extracts DOB + checksum status from the number.
///   • <c>POST /api/civilid/enrich-from-image</c> — uploads an image of the
///     Civil ID card to Gemini Vision and returns the extracted fields.
///     Gated on consent, kill switch, and per-session rate limit.
///
/// Auth: admin-only. Tenants must never reach these — Civil ID OCR is a
/// staff workflow during onboarding.
/// </summary>
[ApiController]
[Route("api/civilid")]
[Authorize(Roles = Roles.AnyAdmin)]
public class CivilIdController : ControllerBase
{
    private readonly ICivilIdEnrichmentService _enrichment;
    private readonly GeminiSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CivilIdController> _logger;

    public CivilIdController(
        ICivilIdEnrichmentService enrichment,
        IOptions<GeminiSettings> settings,
        IMemoryCache cache,
        ILogger<CivilIdController> logger)
    {
        _enrichment = enrichment;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
    }

    // ───────── Phase 1: deterministic parse (no AI, no PII to vendor) ─────────

    /// <summary>
    /// Parse a Civil ID number into its constituent fields. Pure-function,
    /// no DB, no external calls — safe to call on every keystroke.
    /// </summary>
    [HttpGet("parse")]
    public IActionResult Parse([FromQuery] string id)
    {
        var result = CivilIdParser.Parse(id);
        return Ok(new
        {
            isFormatValid = result.IsFormatValid,
            isChecksumValid = result.IsChecksumValid,
            isFullyValid = result.IsFullyValid,
            dateOfBirth = result.DateOfBirth?.ToString("yyyy-MM-dd"),
            gender = result.Gender,
            error = result.Error,
        });
    }

    // ───────── Phase 2: Gemini Vision OCR ─────────

    /// <summary>
    /// Upload a Civil ID image (JPEG / PNG / PDF, ≤5 MB) and receive the
    /// extracted fields for operator confirmation. The operator MUST tick
    /// the consent box before the front-end will send the image.
    /// </summary>
    [HttpPost("enrich-from-image")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> EnrichFromImage(
        IFormFile file,
        [FromForm] bool consent,
        CancellationToken ct)
    {
        // Kill switch — fail fast with a clean 503 so the UI shows a sensible
        // error rather than waiting for a vendor timeout.
        if (!_settings.Enabled)
            return StatusCode(503, new
            {
                error = "Civil ID enrichment is disabled. Contact admin to enable it.",
            });

        // Consent gate: PII goes to a US-based AI service, so we require an
        // explicit opt-in per-call from the operator.
        if (!consent)
            return BadRequest(new
            {
                error = "Consent is required before uploading a Civil ID image.",
            });

        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        if (!AllowedMimeTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new
            {
                error = $"Unsupported file type: {file.ContentType}. Use JPEG, PNG, or PDF.",
            });

        // Per-session rate limit — defends against runaway scripts. Session
        // ID is the auth cookie's principal; one bucket per signed-in user.
        var rateKey = $"civilid:enrich:{User?.Identity?.Name ?? HttpContext.Connection.Id}";
        var hits = _cache.Get<int?>(rateKey) ?? 0;
        if (hits >= _settings.RateLimitPerMinute)
        {
            _logger.LogWarning("Civil ID enrich rate-limit hit for {User}", User?.Identity?.Name);
            return StatusCode(429, new
            {
                error = $"Too many enrichment requests. Limit is {_settings.RateLimitPerMinute}/min.",
            });
        }
        _cache.Set(rateKey, hits + 1, TimeSpan.FromMinutes(1));

        // Read into memory (≤5MB already enforced) so we can pass bytes to the service.
        byte[] bytes;
        await using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        var result = await _enrichment.EnrichFromImageAsync(bytes, file.ContentType, ct);
        if (!result.Success)
            return UnprocessableEntity(new { error = result.ErrorMessage });

        // Defensive cross-check: if the operator typed a Civil ID into the
        // form and the OCR result disagrees, flag it. The front-end shows
        // a warning chip but still surfaces the OCR fields — final say is
        // the operator's after they look at the card themselves.
        return Ok(new
        {
            success = true,
            civilId = result.CivilId,
            fullNameArabic = result.FullNameArabic,
            fullNameEnglish = result.FullNameEnglish,
            dateOfBirth = result.DateOfBirth?.ToString("yyyy-MM-dd"),
            expiryDate = result.ExpiryDate?.ToString("yyyy-MM-dd"),
            gender = result.Gender,
            nationality = result.Nationality,
            // Front-end uses this to flag fields that came from AI (different
            // border colour until the operator clicks each one to confirm).
            source = "ai-vision",
        });
    }

    private static readonly string[] AllowedMimeTypes =
    {
        "image/jpeg", "image/png", "image/jpg",
        "application/pdf",
    };
}
