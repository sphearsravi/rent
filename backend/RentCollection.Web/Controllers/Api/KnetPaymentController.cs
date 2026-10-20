using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Application.DTOs;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.Controllers.Api;

[Route("api/payments/knet")]
[ApiController]
public class KnetPaymentController : ControllerBase
{
    private readonly IKnetPaymentService _knetPaymentService;
    private readonly IPaymentEligibilityService _eligibility;
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<KnetPaymentController> _logger;

    public KnetPaymentController(
        IKnetPaymentService knetPaymentService,
        IPaymentEligibilityService eligibility,
        ApplicationDbContext db,
        IConfiguration configuration,
        ILogger<KnetPaymentController> logger)
    {
        _knetPaymentService = knetPaymentService;
        _eligibility = eligibility;
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Initiates a KNET payment for the tenant
    /// </summary>
    [HttpPost("initiate")]
    [AllowAnonymous]
    public async Task<IActionResult> InitiatePayment([FromBody] KnetPaymentRequestDto request)
    {
        // Sprint 7 kill switch — `Knet:Enabled=false` in config disables
        // payment initiation while leaving the rest of the app live and the
        // response endpoint reachable (so in-flight callbacks still complete
        // before the gate slams shut). Used during gateway maintenance
        // windows or as an emergency stop if fraud is detected.
        var knetEnabled = _configuration.GetValue("Knet:Enabled", defaultValue: true);
        if (!knetEnabled)
        {
            _logger.LogWarning("KNET initiate request rejected — Knet:Enabled=false (kill switch active)");
            return StatusCode(503, new
            {
                error = "Payment gateway temporarily unavailable",
                detail = "K-NET payments are disabled. Please use bank transfer or try again later.",
            });
        }

        // The web tenant portal authenticates with a cookie session; the Flutter app uses
        // a body-supplied TenantId. If a session exists it always wins — the body cannot
        // override the logged-in tenant. Otherwise the service-level ownership check
        // (lease.TenantId == request.TenantId, invoice.LeaseId == lease.LeaseId) guards
        // the body-supplied path.
        var sessionTenantId = HttpContext.Session.GetInt32("TenantId");
        if (sessionTenantId.HasValue)
        {
            request.TenantId = sessionTenantId.Value;
        }
        else if (request.TenantId <= 0)
        {
            return Unauthorized(new { error = "Tenant not logged in" });
        }

        _logger.LogInformation("Initiating KNET payment for tenant {TenantId}, invoice {InvoiceId}",
            request.TenantId, request.InvoiceId);

        // Sprint 24 — oldest-unpaid-first gate. Two-step to avoid leaking
        // OTHER tenants' invoice details: first confirm the invoice belongs
        // to the requesting tenant (otherwise return a generic 404 — same
        // shape we'd return if the invoice didn't exist at all), THEN run
        // the eligibility check on what is genuinely the tenant's queue.
        var invoiceOwner = await _db.Invoices
            .Where(i => i.InvoiceId == request.InvoiceId)
            .Select(i => (int?)i.TenantId)
            .FirstOrDefaultAsync();
        if (invoiceOwner is null || invoiceOwner != request.TenantId)
        {
            _logger.LogWarning(
                "KNET initiate rejected — tenant {TenantId} does not own invoice {InvoiceId}",
                request.TenantId, request.InvoiceId);
            return NotFound(new { error = "Invoice not found" });
        }

        var eligibility = await _eligibility.CheckAsync(request.InvoiceId);
        if (!eligibility.IsEligible)
        {
            _logger.LogWarning(
                "KNET initiate blocked — tenant {TenantId} tried invoice {InvoiceId}, must pay {OldestId} first",
                request.TenantId, request.InvoiceId, eligibility.OldestUnpaidInvoiceId);
            return Conflict(new
            {
                error = "Pay older invoice first",
                detail = eligibility.Reason,
                oldestInvoiceId = eligibility.OldestUnpaidInvoiceId,
                oldestBalance = eligibility.OldestBalance,
            });
        }

        // Red-team #5: without an explicit idempotency key, block spawning a
        // second Pending KNET intent for the same invoice within a short window —
        // stops rapid/concurrent initiates from creating many charge intents for
        // one invoice, while still allowing a genuine retry once a stale intent ages out.
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var recentPending = await _db.Payments.AsNoTracking().AnyAsync(p =>
                p.InvoiceId == request.InvoiceId
                && p.PaymentType == RentCollection.Core.Enums.PaymentType.KNET
                && p.Status == RentCollection.Core.Enums.PaymentStatus.Pending
                && p.CreatedDate > DateTime.UtcNow.AddMinutes(-2));
            if (recentPending)
                return Conflict(new { success = false, message = "A KNET payment is already in progress for this invoice. Please wait a moment before retrying." });
        }

        var result = await _knetPaymentService.InitiatePaymentAsync(request);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Handles the response POST from KNET gateway after payment completion.
    /// KNET posts encrypted trandata and other fields to this endpoint.
    /// </summary>
    [HttpPost("response")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> KnetResponse()
    {
        // KNET posts the result form-urlencoded but with Content-Type: text/html,
        // so [FromForm]/Request.Form comes back EMPTY. Read the raw body (and query)
        // and parse it manually. Without this the handler saw a null trandata,
        // returned "failed", and KNET reversed/refunded the captured payment.
        var fields = await ReadKnetCallbackAsync();
        string? trandata = fields.GetValueOrDefault("trandata");
        string? paymentid = fields.GetValueOrDefault("paymentid");
        string? trackid = fields.GetValueOrDefault("trackid");
        string? tranid = fields.GetValueOrDefault("tranid");
        string? result = fields.GetValueOrDefault("result");
        string? Error = fields.GetValueOrDefault("Error");
        string? ErrorText = fields.GetValueOrDefault("ErrorText");

        _logger.LogInformation("Received KNET response. TrackId: {TrackId}, Result: {Result}, PaymentId: {PaymentId}, trandata={Len}chars",
            trackid, result, paymentid, trandata?.Length ?? 0);

        var response = await _knetPaymentService.HandleKnetResponseAsync(
            trandata, paymentid, trackid, tranid, result, Error, ErrorText);

        // KNET protocol: the responseURL must reply with the plain text
        // "REDIRECT=<absolute-url>" (NOT an HTTP 302). KNET parses that and redirects
        // the customer's browser there. Returning a normal HTTP redirect made KNET
        // treat it as a merchant-side failure and REVERSE/refund the captured payment.
        // SECURITY: do NOT use Request.Host here — this endpoint is [AllowAnonymous]
        // and the Host header is attacker-controllable, which would turn the
        // REDIRECT= reply into an open redirect. Derive the host from the trusted,
        // configured Knet:SuccessUrl instead.
        var successUrl = _configuration["Knet:SuccessUrl"];
        var host = (!string.IsNullOrEmpty(successUrl) && Uri.TryCreate(successUrl, UriKind.Absolute, out var su))
            ? su.GetLeftPart(UriPartial.Authority)
            : $"{Request.Scheme}://{Request.Host}";
        // culture=<lang> localizes the landing page to the payer's app language
        // (threaded from initiate → payment.Notes → response.Language).
        // Use the specific culture — SupportedCultures are ar-KW / en-US, and
        // neutral "ar" won't match a specific-only supported list.
        var culture = response.Language == "ar" ? "ar-KW" : "en-US";
        if (response.Success && response.Status == "Completed")
            return Content($"REDIRECT={host}/TenantPortal/PaymentSuccess?paymentId={response.PaymentId}&reference={response.TransactionReference}&culture={culture}", "text/plain");

        return Content($"REDIRECT={host}/TenantPortal/PaymentFailed?error={Uri.EscapeDataString(response.ErrorMessage ?? "Payment was not completed")}&reference={response.TransactionReference}&culture={culture}", "text/plain");
    }

    /// <summary>
    /// Handles the error callback from KNET gateway (POST with form data)
    /// </summary>
    [HttpPost("error")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> KnetError()
    {
        // Same as the response callback: KNET posts form data as text/html, so read raw.
        var f = await ReadKnetCallbackAsync();
        return await HandleKnetError(f.GetValueOrDefault("trandata"), f.GetValueOrDefault("paymentid"),
            f.GetValueOrDefault("trackid"), f.GetValueOrDefault("tranid"),
            f.GetValueOrDefault("ErrorText"), f.GetValueOrDefault("Error"));
    }

    /// <summary>
    /// Reads a KNET callback's fields from the query string AND the raw request body.
    /// KNET posts form-urlencoded data but with Content-Type: text/html, which makes
    /// ASP.NET's form binding return nothing — so we parse the body ourselves.
    /// </summary>
    private async Task<Dictionary<string, string>> ReadKnetCallbackAsync()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var q in Request.Query) dict[q.Key] = q.Value.ToString();
        string body = "";
        if (Request.HasFormContentType)
        {
            foreach (var fld in Request.Form) dict[fld.Key] = fld.Value.ToString();
        }
        else
        {
            using var reader = new System.IO.StreamReader(Request.Body, System.Text.Encoding.UTF8);
            body = await reader.ReadToEndAsync();
            if (!string.IsNullOrWhiteSpace(body))
            {
                if (body.Contains('='))
                    foreach (var kv in Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(body))
                        dict[kv.Key] = kv.Value.ToString();
                // KNET posts the encrypted response as the RAW hex body (no key=value,
                // no "trandata=" prefix). If we didn't get a trandata field, treat the
                // whole hex body as the trandata blob.
                if (!dict.ContainsKey("trandata") &&
                    System.Text.RegularExpressions.Regex.IsMatch(body.Trim(), "^[0-9A-Fa-f]+$"))
                    dict["trandata"] = body.Trim();
            }
        }
        _logger.LogInformation("KNET callback parsed: CT={CT} bodyLen={L} fields=[{K}]",
            Request.ContentType ?? "(null)", body.Length, string.Join(",", dict.Keys));
        return dict;
    }

    /// <summary>
    /// Handles the error redirect from KNET gateway (GET with query params)
    /// KNET sends GET redirects for validation errors (e.g., invalid UDF fields)
    /// </summary>
    [HttpGet("error")]
    [AllowAnonymous]
    public async Task<IActionResult> KnetErrorGet(
        [FromQuery] string? trandata,
        [FromQuery] string? PaymentID,
        [FromQuery] string? trackid,
        [FromQuery] string? tranid,
        [FromQuery] string? ErrorText,
        [FromQuery] string? Error)
    {
        return await HandleKnetError(trandata, PaymentID, trackid, tranid, ErrorText, Error);
    }

    private async Task<IActionResult> HandleKnetError(
        string? trandata, string? paymentid, string? trackid, string? tranid,
        string? errorText, string? error)
    {
        _logger.LogWarning("Received KNET error callback. TrackId: {TrackId}, Error: {Error}, ErrorText: {ErrorText}",
            trackid, error, errorText);

        var response = await _knetPaymentService.HandleKnetResponseAsync(
            trandata, paymentid, trackid, tranid, "NOT CAPTURED", error, errorText);

        return Redirect($"/TenantPortal/PaymentFailed?error={Uri.EscapeDataString(errorText ?? error ?? "Payment error")}&reference={response.TransactionReference}");
    }

#if DEBUG
    /// <summary>
    /// TEST MODE: Completes a simulated test payment.
    /// SECURITY (audit C2): this endpoint credits an invoice with no gateway
    /// transaction, so it is a full payment-fraud path. It is now (a) compiled
    /// out of Release/production builds entirely via #if DEBUG, (b) gated behind
    /// the Knet:UseTestMode flag at runtime, and (c) requires an authenticated
    /// admin. Never ship a route that can complete a payment without the gateway.
    /// </summary>
    [HttpPost("test/complete")]
    [Authorize(Roles = Roles.AnyAdmin)]
    public async Task<IActionResult> CompleteTestPayment([FromBody] TestPaymentCompleteRequest request)
    {
        if (!_configuration.GetValue<bool>("Knet:UseTestMode"))
            return NotFound();

        _logger.LogWarning("TEST MODE: Completing payment {PaymentId} with success={Success} (by {User})",
            request.PaymentId, request.Success, User.Identity?.Name);

        var result = await _knetPaymentService.CompleteTestPaymentAsync(request.PaymentId, request.Success);
        return result.Success ? Ok(result) : BadRequest(result);
    }
#endif
}

public class TestPaymentCompleteRequest
{
    public int PaymentId { get; set; }
    public bool Success { get; set; } = true;
}
