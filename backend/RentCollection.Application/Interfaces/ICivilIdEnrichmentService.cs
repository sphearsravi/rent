namespace RentCollection.Application.Interfaces;

/// <summary>
/// Reads a Kuwait Civil ID card image and extracts the structured fields.
/// Backed by an external LLM today (Gemini Vision) but the interface keeps
/// the controller decoupled from the vendor — swappable for Azure Document
/// Intelligence, AWS Textract, or an on-premise model if data residency
/// becomes a constraint.
///
/// The service MUST not log the raw image, the prompt body, or the parsed
/// response — PII handling lives at the boundary. Only success/failure +
/// latency + size metrics make it to the log sink.
/// </summary>
public interface ICivilIdEnrichmentService
{
    /// <summary>
    /// Send an image (JPEG / PNG / PDF) to the vision model and parse the
    /// extracted fields into <see cref="CivilIdEnrichmentResult"/>.
    /// Returns a <c>Success=false</c> result rather than throwing for any
    /// recoverable error (timeout, rate-limit, bad image) so the controller
    /// can return a clean 4xx to the operator.
    /// </summary>
    Task<CivilIdEnrichmentResult> EnrichFromImageAsync(
        byte[] imageBytes,
        string mimeType,
        CancellationToken ct = default);
}

/// <summary>
/// Result of a Civil ID OCR pass. All fields are nullable — the model may
/// return partial data on a low-quality scan, and the operator confirms
/// before the form actually saves.
/// </summary>
public sealed record CivilIdEnrichmentResult(
    bool Success,
    string? CivilId,
    string? FullNameArabic,
    string? FullNameEnglish,
    DateTime? DateOfBirth,
    DateTime? ExpiryDate,
    string? Gender,
    string? Nationality,
    string? ErrorMessage);
