using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RentCollection.Application.DTOs;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Core.Enums;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Infrastructure.Services;

public class KnetPaymentService : IKnetPaymentService
{
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<Invoice> _invoiceRepository;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<KnetPaymentService> _logger;
    private readonly KnetSettings _settings;
    private readonly KnetUtil _knetUtil;

    public KnetPaymentService(
        IRepository<Payment> paymentRepository,
        IRepository<Invoice> invoiceRepository,
        ApplicationDbContext context,
        IOptions<KnetSettings> settings,
        KnetUtil knetUtil,
        ILogger<KnetPaymentService> logger)
    {
        _paymentRepository = paymentRepository;
        _invoiceRepository = invoiceRepository;
        _context = context;
        _settings = settings.Value;
        _knetUtil = knetUtil;
        _logger = logger;
    }

    public async Task<KnetPaymentResponseDto> InitiatePaymentAsync(KnetPaymentRequestDto request)
    {
        try
        {
            // Sprint 8 Fix #2 — mask Civil ID in logs. Civil ID is PII in
            // Kuwait; log aggregators (Sentry, OTLP) shouldn't see it in clear.
            _logger.LogInformation("Initiating KNET payment for Invoice {InvoiceId}, Amount: {Amount} KWD, CivilID: {CivilIDMasked}, PropertyId: {PropertyId}",
                request.InvoiceId, request.Amount, MaskCivilId(request.CivilID), request.PropertyId);

            // Step 1: Idempotency Check
            // - Completed: return the prior payment (true replay).
            // - Pending with a cached RedirectUrl: re-issue the same checkout URL so the
            //   user can resume instead of starting a new transaction.
            // - Failed/Cancelled: expire the record and allow a fresh attempt.
            if (!string.IsNullOrEmpty(request.IdempotencyKey))
            {
                // The key is client-generated, so it must be bound to the same
                // invoice — otherwise a replayed/guessed key could return another
                // invoice's payment (or its checkout URL) to this caller.
                var existingIdempotency = await _context.PaymentIdempotencies
                    .FirstOrDefaultAsync(p => p.IdempotencyKey == request.IdempotencyKey
                                           && p.InvoiceId == request.InvoiceId
                                           && p.ExpiresAt > DateTime.UtcNow);

                if (existingIdempotency != null && existingIdempotency.PaymentId.HasValue)
                {
                    var existingPayment = await _paymentRepository.GetByIdAsync(existingIdempotency.PaymentId.Value);

                    if (existingPayment == null)
                    {
                        // Orphan idempotency row — drop it and continue.
                        _context.PaymentIdempotencies.Remove(existingIdempotency);
                        await _context.SaveChangesAsync();
                    }
                    else if (existingPayment.TenantId != request.TenantId)
                    {
                        // Same key + invoice but a different tenant: never hand out
                        // another tenant's payment state. Fail closed.
                        _logger.LogWarning("Idempotency key tenant mismatch. Key={Key}, PaymentTenant={PaymentTenant}, RequestTenant={RequestTenant}",
                            request.IdempotencyKey, existingPayment.TenantId, request.TenantId);
                        return new KnetPaymentResponseDto { Success = false, ErrorMessage = "Invoice ownership cannot be verified" };
                    }
                    else if (existingPayment.Status == PaymentStatus.Completed)
                    {
                        _logger.LogInformation("Idempotent replay (Completed). Key={Key}, PaymentId={PaymentId}",
                            request.IdempotencyKey, existingPayment.PaymentId);
                        return new KnetPaymentResponseDto
                        {
                            Success = true,
                            PaymentId = existingPayment.PaymentId.ToString(),
                            Status = existingPayment.Status.ToString(),
                            TransactionReference = existingPayment.ReceiptNumber,
                            ErrorMessage = "Payment already completed (idempotent request)"
                        };
                    }
                    else if (existingPayment.Status == PaymentStatus.Pending)
                    {
                        _logger.LogInformation("Idempotent replay (Pending). Key={Key}, PaymentId={PaymentId}",
                            request.IdempotencyKey, existingPayment.PaymentId);
                        return new KnetPaymentResponseDto
                        {
                            Success = true,
                            PaymentId = existingPayment.PaymentId.ToString(),
                            RedirectUrl = existingIdempotency.RedirectUrl,
                            Status = existingPayment.Status.ToString(),
                            TransactionReference = existingPayment.TransactionReference
                        };
                    }
                    else
                    {
                        // Failed/Cancelled/other — expire so the caller can retry.
                        _logger.LogInformation("Idempotency record for failed payment expired. Key={Key}, PriorStatus={Status}",
                            request.IdempotencyKey, existingPayment.Status);
                        existingIdempotency.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
                        await _context.SaveChangesAsync();
                    }
                }
            }

            // Step 2: Get invoice
            var invoice = await _invoiceRepository.GetByIdAsync(request.InvoiceId);
            if (invoice == null)
                return new KnetPaymentResponseDto { Success = false, ErrorMessage = "Invoice not found" };

            // Only invoices that are actually collectable may start a KNET
            // payment: Pending, Overdue or Partially Paid. Paid and Cancelled
            // have nothing to collect; "Pending Verification" has a cash/bank
            // payment awaiting approval — paying again by card would double-pay.
            if (string.Equals(invoice.Status, InvoiceStatuses.Paid, StringComparison.OrdinalIgnoreCase))
                return new KnetPaymentResponseDto { Success = false, ErrorMessage = "Invoice already paid" };
            if (string.Equals(invoice.Status, InvoiceStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
                return new KnetPaymentResponseDto { Success = false, ErrorMessage = "Invoice is cancelled and cannot be paid" };
            if (string.Equals(invoice.Status, InvoiceStatuses.PendingVerification, StringComparison.OrdinalIgnoreCase))
                return new KnetPaymentResponseDto
                {
                    Success = false,
                    ErrorMessage = "A cash or bank-transfer payment for this invoice is awaiting verification. Please wait for it to be approved or rejected before paying by KNET."
                };
            if (!string.Equals(invoice.Status, InvoiceStatuses.Pending, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(invoice.Status, InvoiceStatuses.Overdue, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(invoice.Status, InvoiceStatuses.PartiallyPaid, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("KNET initiation blocked — invoice {InvoiceId} in status '{Status}'", invoice.InvoiceId, invoice.Status);
                return new KnetPaymentResponseDto { Success = false, ErrorMessage = "Invoice is not in a payable status" };
            }

            // Validate the requested amount against the remaining balance.
            // KWD is 3 decimal places — round to avoid floating-point surprises.
            var dueAmount = decimal.Round(invoice.TotalAmount - invoice.PaidAmount, 3);
            var requestedAmount = decimal.Round(request.Amount, 3);
            if (requestedAmount <= 0 || requestedAmount > dueAmount)
            {
                _logger.LogWarning("Invalid KNET amount. Requested={Requested}, Due={Due}, InvoiceId={InvoiceId}",
                    requestedAmount, dueAmount, request.InvoiceId);
                return new KnetPaymentResponseDto
                {
                    Success = false,
                    ErrorMessage = $"Invalid amount. Due: {dueAmount:0.000} KWD"
                };
            }

            // Step 3: Get building with KNET config
            var building = await _context.Properties
                .FirstOrDefaultAsync(p => p.PropertyId == request.PropertyId);

            if (building == null)
                return new KnetPaymentResponseDto { Success = false, ErrorMessage = "Building not found" };

            if (!building.AcceptKNET)
                return new KnetPaymentResponseDto { Success = false, ErrorMessage = "This building does not accept KNET payments" };

            // Step 4: Get lease and validate
            var lease = await _context.Leases
                .Include(l => l.Tenant)
                .Include(l => l.Unit)
                    .ThenInclude(u => u.Property)
                .FirstOrDefaultAsync(l => l.LeaseId == (request.LeaseId > 0 ? request.LeaseId : invoice.LeaseId));

            if (lease == null)
                return new KnetPaymentResponseDto { Success = false, ErrorMessage = "Lease not found" };

            // Ownership / cross-reference checks: the invoice, lease, and tenant must all match.
            if (invoice.LeaseId != lease.LeaseId)
            {
                _logger.LogWarning("Lease/Invoice mismatch. InvoiceLease={InvoiceLease}, RequestLease={RequestLease}",
                    invoice.LeaseId, lease.LeaseId);
                return new KnetPaymentResponseDto { Success = false, ErrorMessage = "Lease/Invoice mismatch" };
            }

            // Sprint 8 Fix #3 — fail closed when TenantId is missing or
            // doesn't match the lease's tenant. Previously the check was
            // skipped when TenantId<=0, relying on the controller to gate
            // anonymous calls. Defense in depth: the service must enforce
            // the same invariant so internal callers (admin scripts, future
            // controllers) can't bypass it.
            if (request.TenantId <= 0 || lease.TenantId != request.TenantId)
            {
                _logger.LogWarning("Tenant ownership violation. SessionTenant={SessionTenant}, LeaseTenant={LeaseTenant}, InvoiceId={InvoiceId}",
                    request.TenantId, lease.TenantId, request.InvoiceId);
                return new KnetPaymentResponseDto { Success = false, ErrorMessage = "Invoice ownership cannot be verified" };
            }

            // Step 5: Mismatch detection
            var mismatches = new List<string>();
            var mismatchTypes = new List<string>();
            var hasMismatch = false;

            if (!string.IsNullOrEmpty(request.CivilID) && lease.Tenant != null && lease.Tenant.GovernmentId != request.CivilID)
            {
                mismatches.Add($"CivilID mismatch: Request={request.CivilID}, LeaseOwner={lease.Tenant.GovernmentId}");
                mismatchTypes.Add("CivilIDMismatch");
                hasMismatch = true;
            }

            if (lease.Unit != null && lease.Unit.PropertyId != request.PropertyId)
            {
                mismatches.Add($"Building mismatch: Request={request.PropertyId}, LeaseBuilding={lease.Unit.PropertyId}");
                mismatchTypes.Add("BuildingMismatch");
                hasMismatch = true;
            }

            var mismatchDetails = mismatches.Any() ? string.Join("; ", mismatches) : null;
            var mismatchTypeLabel = mismatchTypes.Any() ? string.Join(",", mismatchTypes) : null;

            // Sprint 8 Fix #1 — append a random suffix to guarantee uniqueness.
            // Two payments on the same invoice in the same second would
            // otherwise produce identical track IDs and the second callback
            // would overwrite the first payment's row. IMPORTANT: KNET's trackid
            // field maxes at 20 chars — the old "INV-{id}-{yyyyMMddHHmmss}-{rand}"
            // was 30 chars and the gateway truncated it, dropping the unique suffix,
            // so repeated attempts for the same invoice collided with
            // "IPAY01000305 - Duplicate Merchant Track Id". Keep it <=20 and unique
            // (invoice id + random hex).
            var trackId = $"{request.InvoiceId}{Guid.NewGuid():N}".ToUpperInvariant();
            if (trackId.Length > 20) trackId = trackId.Substring(0, 20);

            // Step 6: Determine KNET credentials (building-specific or default)
            var tranportalId = !string.IsNullOrEmpty(building.KnetTranportalId) ? building.KnetTranportalId : _settings.TranportalId;
            var tranportalPwd = !string.IsNullOrEmpty(building.KnetTranportalPwd) ? building.KnetTranportalPwd : _settings.TranportalPwd;
            var terminalResourceKey = !string.IsNullOrEmpty(building.KnetTerminalResourceKey) ? building.KnetTerminalResourceKey : _settings.TerminalResourceKey;
            var usesBuildingCredentials = !string.IsNullOrEmpty(building.KnetTranportalId);

            _logger.LogInformation("Using {CredentialType} KNET credentials for Building {BuildingName} (ID: {PropertyId})",
                usesBuildingCredentials ? "building-specific" : "default", building.PropertyName, building.PropertyId);

            // TEST MODE: Simulate payment
            if (_settings.UseTestMode)
            {
                return await InitiateTestPaymentAsync(request, requestedAmount, trackId, lease.LeaseId, building, hasMismatch, mismatchDetails);
            }

            // Step 7: Build KNET redirect URL
            var paymentApiUrl = _settings.UseSandbox ? _settings.PaymentApiUrl : _settings.PaymentApiUrlProd;
            var responseUrl = _settings.SuccessUrl;
            var errorUrl = _settings.ErrorUrl;

            var knetRedirectUrl = _knetUtil.GenerateKnetUrl(
                tranportalId: tranportalId,
                tranportalPwd: tranportalPwd,
                terminalResourceKey: terminalResourceKey,
                paymentApiUrl: paymentApiUrl,
                amount: requestedAmount,   // rounded/validated — must equal what we store on the Payment row
                trackId: trackId,
                responseUrl: responseUrl,
                errorUrl: errorUrl,
                lang: request.Language ?? "en",
                udf1: $"INV-{request.InvoiceId}",
                udf2: $"TNT-{request.TenantId}",
                udf3: request.CivilID ?? "",
                udf4: $"PROP-{request.PropertyId}",
                udf5: $"LSE-{lease.LeaseId}");

            // Log only the TrackId — the URL carries the encrypted trandata + tranportalId.
            _logger.LogDebug("KNET redirect URL generated for TrackId {TrackId}", trackId);

            // Step 8: Create pending payment record - wrapped in transaction for atomicity
            using var transaction = await _context.Database.BeginTransactionAsync();
            var now = DateTime.UtcNow;
            var payment = new Payment
            {
                InvoiceId = request.InvoiceId,
                LeaseId = lease.LeaseId,
                TenantId = request.TenantId,
                PropertyId = request.PropertyId,
                // The callback compares the gateway `amt` to this value with exact
                // equality, so it MUST be the same rounded figure sent to KNET.
                AmountPaid = requestedAmount,
                PaymentDate = now,
                PaymentType = PaymentType.KNET,
                Status = PaymentStatus.Pending,
                TransactionReference = trackId,
                // ReceiptNumber is intentionally left null until KNET captures the payment.
                CreatedDate = now,
                CivilID = request.CivilID,
                KNETAccountIDUsed = building.KNETAccountID,
                KNETMIDUsed = building.KNETMID,
                IdempotencyKey = request.IdempotencyKey,
                HasMismatch = hasMismatch,
                MismatchDetails = mismatchDetails,
                // The [lang=..] marker lets the callback localize the
                // success/failure page to the payer's app language.
                Notes = $"KNET Payment initiated at {now:yyyy-MM-dd HH:mm:ss} UTC for Building: {building.PropertyName} [lang={(request.Language == "ar" ? "ar" : "en")}]"
            };

            await _paymentRepository.AddAsync(payment);

            // Create idempotency record (with the gateway URL cached for resume)
            if (!string.IsNullOrEmpty(request.IdempotencyKey))
            {
                _context.PaymentIdempotencies.Add(new PaymentIdempotency
                {
                    IdempotencyKey = request.IdempotencyKey,
                    PaymentId = payment.PaymentId,
                    InvoiceId = request.InvoiceId,
                    LeaseId = lease.LeaseId,
                    Amount = requestedAmount,
                    CivilID = request.CivilID,
                    Status = "Pending",
                    RedirectUrl = knetRedirectUrl,
                    CreatedAt = now,
                    ExpiresAt = now.AddHours(24)
                });
                await _context.SaveChangesAsync();
            }

            // Create audit record if mismatch
            if (hasMismatch)
            {
                _context.PaymentAudits.Add(new PaymentAudit
                {
                    PaymentId = payment.PaymentId,
                    AuditType = "MismatchDetected",
                    AuditTimestamp = now,
                    Amount = requestedAmount,
                    IsMismatch = true,
                    MismatchType = mismatchTypeLabel,
                    ExpectedCivilID = lease.Tenant?.GovernmentId,
                    ActualCivilID = request.CivilID,
                    ExpectedBuildingId = lease.Unit?.PropertyId ?? 0,
                    ActualBuildingId = request.PropertyId,
                    MismatchDetails = mismatchDetails,
                    IsResolved = false
                });
                await _context.SaveChangesAsync();
            }

            await transaction.CommitAsync();

            _logger.LogInformation("KNET payment initiated. TrackId: {TrackId}, Building: {Building}, HasMismatch: {HasMismatch}",
                trackId, building.PropertyName, hasMismatch);

            return new KnetPaymentResponseDto
            {
                Success = true,
                PaymentId = payment.PaymentId.ToString(),
                RedirectUrl = knetRedirectUrl,
                Status = "Pending",
                TransactionReference = trackId,
                PropertyId = building.PropertyId,
                BuildingName = building.PropertyName,
                KNETAccountID = building.KNETAccountID,
                KNETMID = building.KNETMID,
                HasMismatch = hasMismatch,
                MismatchDetails = mismatchDetails
            };
        }
        catch (Exception ex)
        {
            // Anonymous endpoint — never echo ex.Message (SQL text, connection
            // strings, stack hints) to the caller. Full detail goes to the log.
            _logger.LogError(ex, "Error initiating KNET payment for Invoice {InvoiceId}", request.InvoiceId);
            return new KnetPaymentResponseDto { Success = false, ErrorMessage = "Payment processing error" };
        }
    }

    public async Task<KnetPaymentResponseDto> HandleKnetResponseAsync(
        string? trandata,
        string? paymentId,
        string? trackId,
        string? tranId,
        string? result,
        string? error,
        string? errorText = null)
    {
        try
        {
            _logger.LogInformation("Handling KNET response. TrackId: {TrackId}, Result: {Result}, PaymentId: {PaymentId}, TranId: {TranId}",
                trackId, result, paymentId, tranId);

            // Decrypt trandata if provided
            KnetGatewayResponseDto gatewayResponse;
            if (!string.IsNullOrEmpty(trandata))
            {
                // Sprint 8 Fix #4 — key fallback. Build a list of candidate
                // keys (building-specific first, then default) and try each
                // until decryption produces parseable params. The previous
                // code picked a single key based on payment lookup and
                // failed if the wrong one was chosen (e.g. callback arrives
                // before the Payment row is committed → falls back to default
                // → fails to decrypt building-encrypted blob).
                var candidateKeys = new List<string>();
                if (!string.IsNullOrEmpty(trackId))
                {
                    var existingPayment = (await _paymentRepository.GetAllAsync(p => p.TransactionReference == trackId)).FirstOrDefault();
                    if (existingPayment != null)
                    {
                        var building = await _context.Properties.FirstOrDefaultAsync(p => p.PropertyId == existingPayment.PropertyId);
                        if (building != null && !string.IsNullOrEmpty(building.KnetTerminalResourceKey))
                            candidateKeys.Add(building.KnetTerminalResourceKey);
                    }
                }
                if (!string.IsNullOrEmpty(_settings.TerminalResourceKey))
                    candidateKeys.Add(_settings.TerminalResourceKey);

                if (candidateKeys.Count == 0)
                {
                    _logger.LogError("No terminal resource keys configured — cannot decrypt KNET callback. TrackId: {TrackId}", trackId);
                    return new KnetPaymentResponseDto { Success = false, ErrorMessage = "Gateway configuration incomplete" };
                }

                string? decrypted = null;
                Exception? lastError = null;
                foreach (var key in candidateKeys.Distinct())
                {
                    try
                    {
                        decrypted = _knetUtil.DecryptUrlParams(trandata, key);
                        // Successful decrypt produces ASCII key=value pairs;
                        // garbled output won't contain '=' or 'result' marker.
                        if (decrypted.Contains('='))
                            break;
                        decrypted = null;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        decrypted = null;
                    }
                }

                if (decrypted == null)
                {
                    _logger.LogError(lastError, "All decryption attempts failed for KNET callback. TrackId: {TrackId}, KeysTried: {KeyCount}",
                        trackId, candidateKeys.Distinct().Count());
                    return new KnetPaymentResponseDto { Success = false, ErrorMessage = "Callback decryption failed" };
                }

                var parsedParams = _knetUtil.ParseResponseParams(decrypted);
                // NOTE: never log the decrypted params — they contain the customer's
                // Civil ID (udf3), auth code, tran ref, etc. (PII).

                gatewayResponse = new KnetGatewayResponseDto
                {
                    Result = parsedParams.GetValueOrDefault("result") ?? result,
                    PaymentId = parsedParams.GetValueOrDefault("paymentid") ?? paymentId,
                    TransactionId = parsedParams.GetValueOrDefault("tranid") ?? tranId,
                    TrackId = parsedParams.GetValueOrDefault("trackid") ?? trackId,
                    AuthCode = parsedParams.GetValueOrDefault("auth"),
                    ReferenceId = parsedParams.GetValueOrDefault("ref"),
                    PostDate = parsedParams.GetValueOrDefault("postdate"),
                    Udf1 = parsedParams.GetValueOrDefault("udf1"),
                    Udf2 = parsedParams.GetValueOrDefault("udf2"),
                    Udf3 = parsedParams.GetValueOrDefault("udf3"),
                    Udf4 = parsedParams.GetValueOrDefault("udf4"),
                    Udf5 = parsedParams.GetValueOrDefault("udf5"),
                    Error = parsedParams.GetValueOrDefault("Error") ?? error,
                    ErrorText = parsedParams.GetValueOrDefault("ErrorText")
                };

                // Culture-invariant: KNET always sends "1500.000". Under an ar-KW
                // or de-DE thread culture the default TryParse would read the '.'
                // as a thousands separator → 1500000 → amount check fails (or
                // worse, passes for a different value).
                if (decimal.TryParse(parsedParams.GetValueOrDefault("amt"), NumberStyles.Number, CultureInfo.InvariantCulture, out var amt))
                    gatewayResponse.Amount = amt;
            }
            else
            {
                // SECURITY (audit C1): a genuine KNET callback ALWAYS delivers an
                // encrypted `trandata` blob (the raw hex POST body). A callback with
                // no trandata carries only forgeable cleartext query params
                // (result/trackid) — trusting it let an unauthenticated attacker POST
                // `trackid=<known>&result=CAPTURED` and mark an invoice paid for free.
                // Reject outright; never build a capture from untrusted fields.
                _logger.LogWarning(
                    "KNET callback WITHOUT trandata rejected as unauthenticated. TrackId: {TrackId}, Result: {Result}",
                    trackId, result);
                return new KnetPaymentResponseDto
                {
                    Success = false,
                    ErrorMessage = "Unauthenticated callback rejected"
                };
            }

            // Find our payment record by trackId
            var resolvedTrackId = gatewayResponse.TrackId ?? trackId;
            if (string.IsNullOrEmpty(resolvedTrackId))
            {
                return new KnetPaymentResponseDto { Success = false, ErrorMessage = "Missing track ID in KNET response" };
            }

            var payments = await _paymentRepository.GetAllAsync(p => p.TransactionReference == resolvedTrackId);
            var payment = payments.FirstOrDefault();

            if (payment == null)
            {
                _logger.LogWarning("Payment not found for TrackId: {TrackId}", resolvedTrackId);
                return new KnetPaymentResponseDto { Success = false, ErrorMessage = "Payment record not found" };
            }

            // Language the payer used (stashed in Notes at initiate as [lang=..])
            // — drives the localized success/failure landing page.
            var lang = (payment.Notes ?? "").Contains("[lang=ar]") ? "ar" : "en";

            // Idempotency guard: KNET (and browser retries) can deliver the same callback multiple times.
            // Process only when the payment is still Pending; otherwise return the existing outcome.
            if (payment.Status != PaymentStatus.Pending)
            {
                _logger.LogInformation("Duplicate KNET callback for TrackId {TrackId} ignored — payment already {Status}",
                    resolvedTrackId, payment.Status);

                var alreadyCompleted = payment.Status == PaymentStatus.Completed;
                return new KnetPaymentResponseDto
                {
                    Success = alreadyCompleted,
                    PaymentId = payment.PaymentId.ToString(),
                    KnetPaymentId = payment.CheckoutPaymentId,
                    Status = payment.Status.ToString(),
                    TransactionReference = payment.ReceiptNumber,
                    ErrorMessage = alreadyCompleted ? null : "Payment already processed",
                    Language = lang
                };
            }

            var isCaptured = string.Equals(gatewayResponse.Result, "CAPTURED", StringComparison.OrdinalIgnoreCase);

            // Reject capture unless the gateway-reported amount is PRESENT and
            // exactly equals our payment record. Sprint 8 Fix #5 tightened this to
            // exact equality; audit C1 further requires the amount to EXIST — a
            // CAPTURED result with no decrypted `amt` must fail closed rather than
            // be credited for the full pending amount. KWD is fil-level precision
            // and K-NET reports the gross amount, so we expect bit-perfect agreement.
            if (isCaptured && (!gatewayResponse.Amount.HasValue
                || gatewayResponse.Amount.Value != payment.AmountPaid))
            {
                _logger.LogWarning(
                    "KNET amount check failed on TrackId {TrackId}. Expected {Expected} KWD, gateway returned {Actual}",
                    resolvedTrackId, payment.AmountPaid,
                    gatewayResponse.Amount.HasValue ? gatewayResponse.Amount.Value.ToString("0.000") : "(missing)");

                payment.Status = PaymentStatus.Failed;
                payment.CheckoutPaymentId = gatewayResponse.PaymentId;
                payment.ModifiedDate = DateTime.UtcNow;
                // The gateway DID report CAPTURED — flag it so reconciliation looks
                // at it even though we refused to credit the invoice.
                payment.HasMismatch = true;
                payment.MismatchDetails = string.IsNullOrEmpty(payment.MismatchDetails)
                    ? "AmountMismatch: gateway CAPTURED amount differs from payment record"
                    : payment.MismatchDetails + "; AmountMismatch: gateway CAPTURED amount differs from payment record";
                payment.Notes = (payment.Notes ?? "") +
                    $"\nAmount mismatch at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC. " +
                    $"Expected: {payment.AmountPaid:0.000} KWD, Gateway: {gatewayResponse.Amount:0.000} KWD";
                await using (var mismatchTx = await _context.Database.BeginTransactionAsync())
                {
                    await _paymentRepository.UpdateAsync(payment);
                    await SyncIdempotencyStatusAsync(payment);
                    await mismatchTx.CommitAsync();
                }

                return new KnetPaymentResponseDto
                {
                    Success = false,
                    PaymentId = payment.PaymentId.ToString(),
                    KnetPaymentId = gatewayResponse.PaymentId,
                    Status = payment.Status.ToString(),
                    TransactionReference = payment.ReceiptNumber,
                    ErrorMessage = "Amount mismatch — payment rejected",
                    Language = lang
                };
            }

            // ── Step 1: persist the PAYMENT outcome first, in its own commit ──
            // The gateway has already moved (or declined) the money; that fact
            // must be durable BEFORE the invoice is touched. Previously payment +
            // invoice shared one transaction, so an invoice RowVersion conflict
            // (concurrent cash approval, overdue flip, second KNET callback)
            // rolled back the Completed status AND the KNET PaymentId/TranId —
            // the only evidence we held that the customer was charged.
            payment.CheckoutPaymentId = gatewayResponse.PaymentId; // Store KNET payment ID
            payment.ModifiedDate = DateTime.UtcNow;

            if (isCaptured)
            {
                payment.Status = PaymentStatus.Completed;
                payment.ApprovedDate = DateTime.UtcNow;
                // Issue the receipt number now that the payment has actually captured.
                payment.ReceiptNumber = $"RCPT-{payment.PaymentId:D8}";
                payment.Notes = (payment.Notes ?? "") +
                    $"\nKNET Payment CAPTURED at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC. " +
                    $"TranId: {gatewayResponse.TransactionId}, Auth: {gatewayResponse.AuthCode}, Ref: {gatewayResponse.ReferenceId}";
            }
            else
            {
                payment.Status = PaymentStatus.Failed;
                payment.Notes = (payment.Notes ?? "") +
                    $"\nKNET Payment FAILED at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC. " +
                    $"Result: {gatewayResponse.Result}, Error: {gatewayResponse.Error} {gatewayResponse.ErrorText}";
            }

            await using (var paymentTx = await _context.Database.BeginTransactionAsync())
            {
                await _paymentRepository.UpdateAsync(payment);
                // Sync the idempotency record so a replay returns the final status.
                await SyncIdempotencyStatusAsync(payment);
                await paymentTx.CommitAsync();
            }

            _logger.LogInformation("Payment {PaymentId} updated to status {Status}. KNET Result: {Result}",
                payment.PaymentId, payment.Status, gatewayResponse.Result);

            // ── Step 2: credit the invoice (captured only) with retry ──
            // Runs AFTER the payment commit. If it ultimately fails the payment
            // stays Completed and is flagged for manual reconciliation — the
            // money was taken, so the record must say so.
            if (isCaptured && payment.InvoiceId.HasValue)
                await CreditInvoiceForCapturedPaymentAsync(payment, resolvedTrackId);

            return new KnetPaymentResponseDto
            {
                Success = isCaptured,
                PaymentId = payment.PaymentId.ToString(),
                KnetPaymentId = gatewayResponse.PaymentId,
                Status = payment.Status.ToString(),
                TransactionReference = payment.ReceiptNumber,
                ErrorMessage = isCaptured ? null : $"{gatewayResponse.Result}: {gatewayResponse.Error} {gatewayResponse.ErrorText}",
                Language = lang
            };
        }
        catch (Exception ex)
        {
            // Anonymous gateway callback — generic message to the caller, detail to the log.
            _logger.LogError(ex, "Error handling KNET response for TrackId: {TrackId}", trackId);
            return new KnetPaymentResponseDto { Success = false, ErrorMessage = "Payment processing error" };
        }
    }

    /// <summary>Appended to Payment.Notes/MismatchDetails when a captured payment
    /// could not be applied to its invoice. Reconciliation searches for it.</summary>
    private const string CreditFailedMarker = "Captured but invoice credit failed — needs manual reconciliation";

    /// <summary>
    /// Apply an already-COMMITTED captured payment to its invoice.
    ///
    ///  • Overpayment protection: credits at most the outstanding balance
    ///    (TotalAmount − PaidAmount). PaidAmount can therefore never exceed
    ///    TotalAmount; any excess is recorded on the payment as a mismatch
    ///    ("refund/credit required") rather than inflating the invoice.
    ///  • Concurrency: up to 3 attempts. A <see cref="DbUpdateConcurrencyException"/>
    ///    (RowVersion) means someone else just changed the invoice — reload it
    ///    and recompute against the fresh balance.
    ///  • Give-up path: the payment stays Completed, gets HasMismatch=true and
    ///    the <see cref="CreditFailedMarker"/>; an error is logged. Nothing is
    ///    rolled back — the payment commit already happened and must stand.
    /// </summary>
    private async Task CreditInvoiceForCapturedPaymentAsync(Payment payment, string? trackId)
    {
        const int maxAttempts = 3;
        var invoiceId = payment.InvoiceId!.Value;
        Invoice? invoice = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (invoice == null)
                    invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.InvoiceId == invoiceId);
                else
                    // Retry: pull the current PaidAmount/Status/RowVersion and
                    // discard our stale in-memory edit before recomputing.
                    await _context.Entry(invoice).ReloadAsync();

                if (invoice == null)
                {
                    _logger.LogError("KNET capture {PaymentId} (TrackId {TrackId}) references missing invoice {InvoiceId}",
                        payment.PaymentId, trackId, invoiceId);
                    await FlagCreditFailureAsync(payment, "invoice not found");
                    return;
                }

                // Overpayment protection — never push PaidAmount past TotalAmount.
                var balance = decimal.Round(invoice.TotalAmount - invoice.PaidAmount, 3);
                var credit = payment.AmountPaid;
                if (credit > balance)
                {
                    var excess = decimal.Round(credit - balance, 3);
                    credit = Math.Max(0m, balance);
                    var msg = $"Overpayment of {excess:0.000} KWD captured — refund/credit required";
                    payment.HasMismatch = true;
                    payment.MismatchDetails = string.IsNullOrEmpty(payment.MismatchDetails)
                        ? msg
                        : payment.MismatchDetails + "; " + msg;
                    payment.Notes = (payment.Notes ?? "") +
                        $"\n{msg} (invoice {invoice.InvoiceId}: balance was {balance:0.000} KWD, captured {payment.AmountPaid:0.000} KWD)";
                    _logger.LogWarning(
                        "KNET overpayment on invoice {InvoiceId}: captured {Captured:0.000} KWD against balance {Balance:0.000} KWD " +
                        "(excess {Excess:0.000}). Payment {PaymentId} flagged; invoice credited only up to balance.",
                        invoice.InvoiceId, payment.AmountPaid, balance, excess, payment.PaymentId);
                }

                invoice.PaidAmount += credit;
                invoice.Status = invoice.PaidAmount >= invoice.TotalAmount ? InvoiceStatuses.Paid : InvoiceStatuses.PartiallyPaid;
                // Saves the invoice AND any mismatch flags set on the (tracked) payment.
                await _context.SaveChangesAsync();
                return;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex,
                    "Invoice {InvoiceId} RowVersion conflict while crediting payment {PaymentId} (attempt {Attempt}/{Max})",
                    invoiceId, payment.PaymentId, attempt, maxAttempts);
                if (attempt == maxAttempts)
                {
                    // Drop the stale invoice edit so the flag save below doesn't re-throw on it.
                    if (invoice != null) _context.Entry(invoice).State = EntityState.Detached;
                    await FlagCreditFailureAsync(payment, $"RowVersion conflict after {maxAttempts} attempts");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error crediting invoice {InvoiceId} for payment {PaymentId}",
                    invoiceId, payment.PaymentId);
                if (invoice != null) _context.Entry(invoice).State = EntityState.Detached;
                await FlagCreditFailureAsync(payment, ex.GetType().Name);
                return;
            }
        }
    }

    /// <summary>Mark a Completed payment whose invoice credit failed so
    /// reconciliation picks it up. Best-effort — never throws.</summary>
    private async Task FlagCreditFailureAsync(Payment payment, string reason)
    {
        payment.HasMismatch = true;
        payment.MismatchDetails = string.IsNullOrEmpty(payment.MismatchDetails)
            ? CreditFailedMarker
            : payment.MismatchDetails + "; " + CreditFailedMarker;
        payment.Notes = (payment.Notes ?? "") +
            $"\n{CreditFailedMarker} ({reason}) at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC";
        payment.ModifiedDate = DateTime.UtcNow;

        _logger.LogError(
            "Payment {PaymentId} is Completed (KNET captured) but invoice {InvoiceId} was NOT credited: {Reason}. Manual reconciliation required.",
            payment.PaymentId, payment.InvoiceId, reason);

        try
        {
            await _paymentRepository.UpdateAsync(payment);
        }
        catch (Exception ex)
        {
            // The log line above is the fallback evidence trail.
            _logger.LogError(ex, "Could not persist reconciliation flag on payment {PaymentId}", payment.PaymentId);
        }
    }

    /// <summary>
    /// TEST MODE: Simulates KNET payment without calling the KNET gateway
    /// </summary>
    private async Task<KnetPaymentResponseDto> InitiateTestPaymentAsync(
        KnetPaymentRequestDto request,
        decimal requestedAmount,   // rounded/validated by the caller
        string trackId,
        int leaseId,
        Property building,
        bool hasMismatch,
        string? mismatchDetails)
    {
        _logger.LogInformation("TEST MODE: Simulating KNET payment for Invoice {InvoiceId}, Amount: {Amount} KWD, Building: {Building}",
            request.InvoiceId, requestedAmount, building.PropertyName);

        using var transaction = await _context.Database.BeginTransactionAsync();
        var now = DateTime.UtcNow;
        var payment = new Payment
        {
            InvoiceId = request.InvoiceId,
            LeaseId = leaseId,
            TenantId = request.TenantId,
            PropertyId = request.PropertyId,
            AmountPaid = requestedAmount,
            PaymentDate = now,
            PaymentType = PaymentType.KNET,
            Status = PaymentStatus.Pending,
            TransactionReference = trackId,
            CheckoutPaymentId = $"test_knet_{Guid.NewGuid():N}",
            // ReceiptNumber is set only after the simulated payment completes successfully.
            CreatedDate = now,
            CivilID = request.CivilID,
            KNETAccountIDUsed = building.KNETAccountID,
            KNETMIDUsed = building.KNETMID,
            IdempotencyKey = request.IdempotencyKey,
            HasMismatch = hasMismatch,
            MismatchDetails = mismatchDetails,
            Notes = $"KNET Test Payment initiated at {now:yyyy-MM-dd HH:mm:ss} UTC for Building: {building.PropertyName}"
        };

        await _paymentRepository.AddAsync(payment);

        // Build the test redirect URL up-front so we can cache it on the idempotency row.
        var testBaseUri = new Uri(_settings.SuccessUrl);
        var testPaymentUrl = $"{testBaseUri.Scheme}://{testBaseUri.Authority}/TenantPortal/TestPayment?paymentId={payment.PaymentId}&knetPaymentId={payment.CheckoutPaymentId}&amount={requestedAmount.ToString("0.000", CultureInfo.InvariantCulture)}&invoiceId={request.InvoiceId}";

        // Create idempotency record (with the test redirect URL cached for resume)
        if (!string.IsNullOrEmpty(request.IdempotencyKey))
        {
            _context.PaymentIdempotencies.Add(new PaymentIdempotency
            {
                IdempotencyKey = request.IdempotencyKey,
                PaymentId = payment.PaymentId,
                InvoiceId = request.InvoiceId,
                LeaseId = leaseId,
                Amount = requestedAmount,
                CivilID = request.CivilID,
                Status = "Pending",
                RedirectUrl = testPaymentUrl,
                CreatedAt = now,
                ExpiresAt = now.AddHours(24)
            });
            await _context.SaveChangesAsync();
        }

        // Create audit record if mismatch
        if (hasMismatch)
        {
            _context.PaymentAudits.Add(new PaymentAudit
            {
                PaymentId = payment.PaymentId,
                AuditType = "MismatchDetected",
                AuditTimestamp = now,
                Amount = requestedAmount,
                IsMismatch = true,
                MismatchType = "CivilID/Building",
                MismatchDetails = mismatchDetails,
                IsResolved = false
            });
            await _context.SaveChangesAsync();
        }

        await transaction.CommitAsync();

        _logger.LogInformation("TEST MODE: Payment record created. PaymentId: {PaymentId}, TrackId: {TrackId}, Building: {Building}",
            payment.PaymentId, trackId, building.PropertyName);

        return new KnetPaymentResponseDto
        {
            Success = true,
            PaymentId = payment.PaymentId.ToString(),
            KnetPaymentId = payment.CheckoutPaymentId,
            RedirectUrl = testPaymentUrl,
            Status = "Pending",
            TransactionReference = trackId,
            PropertyId = building.PropertyId,
            BuildingName = building.PropertyName,
            KNETAccountID = building.KNETAccountID,
            KNETMID = building.KNETMID,
            HasMismatch = hasMismatch,
            MismatchDetails = mismatchDetails
        };
    }

    public async Task<KnetPaymentResponseDto> CompleteTestPaymentAsync(int paymentId, bool success = true)
    {
        try
        {
            _logger.LogInformation("TEST MODE: Completing payment {PaymentId} with success={Success}", paymentId, success);

            var payments = await _paymentRepository.GetAllAsync(p => p.PaymentId == paymentId);
            var payment = payments.FirstOrDefault();

            if (payment == null)
                return new KnetPaymentResponseDto { Success = false, ErrorMessage = "Payment not found" };

            // Same idempotency guard as the live callback — a double-click on the
            // test page must not credit the invoice twice.
            if (payment.Status != PaymentStatus.Pending)
            {
                _logger.LogInformation("TEST MODE: payment {PaymentId} already {Status} — ignoring repeat completion", paymentId, payment.Status);
                return new KnetPaymentResponseDto
                {
                    Success = payment.Status == PaymentStatus.Completed,
                    PaymentId = payment.PaymentId.ToString(),
                    KnetPaymentId = payment.CheckoutPaymentId,
                    Status = payment.Status.ToString(),
                    TransactionReference = payment.ReceiptNumber,
                    ErrorMessage = payment.Status == PaymentStatus.Completed ? null : "Payment already processed"
                };
            }

            var completedAt = DateTime.UtcNow;
            payment.ModifiedDate = completedAt;

            if (success)
            {
                payment.Status = PaymentStatus.Completed;
                payment.ApprovedDate = completedAt;
                // Receipt number is issued at the moment of capture.
                payment.ReceiptNumber = $"RCPT-{payment.PaymentId:D8}";
                payment.Notes = (payment.Notes ?? "") + $"\nPayment completed successfully at {completedAt:yyyy-MM-dd HH:mm:ss} UTC";
            }
            else
            {
                payment.Status = PaymentStatus.Failed;
                payment.Notes = (payment.Notes ?? "") + $"\nPayment failed at {completedAt:yyyy-MM-dd HH:mm:ss} UTC";
            }

            // Mirror the live path: commit the payment outcome first, then credit
            // the invoice with overpayment protection + retry.
            await using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                await _paymentRepository.UpdateAsync(payment);
                await SyncIdempotencyStatusAsync(payment);
                await transaction.CommitAsync();
            }

            if (success && payment.InvoiceId.HasValue)
                await CreditInvoiceForCapturedPaymentAsync(payment, payment.TransactionReference);

            _logger.LogInformation("TEST MODE: Payment {PaymentId} completed with status {Status}", paymentId, payment.Status);

            return new KnetPaymentResponseDto
            {
                Success = true,
                PaymentId = payment.PaymentId.ToString(),
                KnetPaymentId = payment.CheckoutPaymentId,
                Status = payment.Status.ToString(),
                TransactionReference = payment.ReceiptNumber
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TEST MODE: Error completing payment {PaymentId}", paymentId);
            return new KnetPaymentResponseDto { Success = false, ErrorMessage = "Payment processing error" };
        }
    }

    /// <summary>
    /// Mirrors the payment's terminal status onto its idempotency row so that
    /// idempotent replays return the correct outcome.
    /// </summary>
    private async Task SyncIdempotencyStatusAsync(Payment payment)
    {
        if (string.IsNullOrEmpty(payment.IdempotencyKey))
            return;

        var record = await _context.PaymentIdempotencies
            .FirstOrDefaultAsync(p => p.IdempotencyKey == payment.IdempotencyKey);
        if (record == null)
            return;

        record.Status = payment.Status.ToString();
        record.IsProcessed = payment.Status != PaymentStatus.Pending;
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Sprint 8 Fix #2 — mask all but the last 4 characters of a Civil ID
    /// for log output. Civil ID is PII in Kuwait and our log sinks (Sentry,
    /// OTLP collector, file appenders) shouldn't see it in clear.
    ///
    /// Exposed as <c>internal static</c> so regression tests can pin the
    /// masking shape without going through a service instance.
    /// </summary>
    internal static string MaskCivilId(string? civilId)
    {
        if (string.IsNullOrEmpty(civilId)) return "(empty)";
        if (civilId.Length <= 4) return new string('*', civilId.Length);
        return new string('*', civilId.Length - 4) + civilId.Substring(civilId.Length - 4);
    }
}
