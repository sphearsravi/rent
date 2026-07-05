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
                var existingIdempotency = await _context.PaymentIdempotencies
                    .FirstOrDefaultAsync(p => p.IdempotencyKey == request.IdempotencyKey && p.ExpiresAt > DateTime.UtcNow);

                if (existingIdempotency != null && existingIdempotency.PaymentId.HasValue)
                {
                    var existingPayment = await _paymentRepository.GetByIdAsync(existingIdempotency.PaymentId.Value);

                    if (existingPayment == null)
                    {
                        // Orphan idempotency row — drop it and continue.
                        _context.PaymentIdempotencies.Remove(existingIdempotency);
                        await _context.SaveChangesAsync();
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

            // Reject already-paid invoices.
            if (string.Equals(invoice.Status, "Paid", StringComparison.OrdinalIgnoreCase))
                return new KnetPaymentResponseDto { Success = false, ErrorMessage = "Invoice already paid" };

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
                return await InitiateTestPaymentAsync(request, trackId, lease.LeaseId, building, hasMismatch, mismatchDetails);
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
                amount: request.Amount,
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
                AmountPaid = request.Amount,
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
                Notes = $"KNET Payment initiated at {now:yyyy-MM-dd HH:mm:ss} UTC for Building: {building.PropertyName}"
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
                    Amount = request.Amount,
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
                    Amount = request.Amount,
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
            _logger.LogError(ex, "Error initiating KNET payment for Invoice {InvoiceId}", request.InvoiceId);
            return new KnetPaymentResponseDto { Success = false, ErrorMessage = ex.Message };
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

                if (decimal.TryParse(parsedParams.GetValueOrDefault("amt"), out var amt))
                    gatewayResponse.Amount = amt;
            }
            else
            {
                gatewayResponse = new KnetGatewayResponseDto
                {
                    Result = result,
                    PaymentId = paymentId,
                    TransactionId = tranId,
                    TrackId = trackId,
                    Error = error,
                    ErrorText = errorText
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
                    ErrorMessage = alreadyCompleted ? null : "Payment already processed"
                };
            }

            // Update payment with KNET response data - wrapped in transaction for atomicity
            using var transaction = await _context.Database.BeginTransactionAsync();
            var isCaptured = string.Equals(gatewayResponse.Result, "CAPTURED", StringComparison.OrdinalIgnoreCase);

            // Reject capture when gateway-reported amount disagrees with our
            // payment record. Sprint 8 Fix #5 — tightened from "> 0.001" (which
            // allowed a 1-fil drift to slip through) to exact equality. KWD
            // is fil-level precision and K-NET reports the gross amount, not
            // a net-of-fees amount, so we expect bit-perfect agreement.
            if (isCaptured && gatewayResponse.Amount.HasValue
                && gatewayResponse.Amount.Value != payment.AmountPaid)
            {
                _logger.LogWarning(
                    "KNET amount mismatch on TrackId {TrackId}. Expected {Expected} KWD, gateway returned {Actual} KWD",
                    resolvedTrackId, payment.AmountPaid, gatewayResponse.Amount);

                payment.Status = PaymentStatus.Failed;
                payment.CheckoutPaymentId = gatewayResponse.PaymentId;
                payment.ModifiedDate = DateTime.UtcNow;
                payment.Notes = (payment.Notes ?? "") +
                    $"\nAmount mismatch at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC. " +
                    $"Expected: {payment.AmountPaid:0.000} KWD, Gateway: {gatewayResponse.Amount:0.000} KWD";
                await _paymentRepository.UpdateAsync(payment);
                await SyncIdempotencyStatusAsync(payment);
                await transaction.CommitAsync();

                return new KnetPaymentResponseDto
                {
                    Success = false,
                    PaymentId = payment.PaymentId.ToString(),
                    KnetPaymentId = gatewayResponse.PaymentId,
                    Status = payment.Status.ToString(),
                    TransactionReference = payment.ReceiptNumber,
                    ErrorMessage = "Amount mismatch — payment rejected"
                };
            }

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

                // Update invoice
                if (payment.InvoiceId.HasValue)
                {
                    var invoice = await _invoiceRepository.GetByIdAsync(payment.InvoiceId.Value);
                    if (invoice != null)
                    {
                        invoice.PaidAmount += payment.AmountPaid;
                        invoice.Status = invoice.PaidAmount >= invoice.TotalAmount ? "Paid" : "Partially Paid";
                        await _invoiceRepository.UpdateAsync(invoice);
                    }
                }
            }
            else
            {
                payment.Status = PaymentStatus.Failed;
                payment.Notes = (payment.Notes ?? "") +
                    $"\nKNET Payment FAILED at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC. " +
                    $"Result: {gatewayResponse.Result}, Error: {gatewayResponse.Error} {gatewayResponse.ErrorText}";
            }

            await _paymentRepository.UpdateAsync(payment);

            // Sync the idempotency record so a replay returns the final status.
            await SyncIdempotencyStatusAsync(payment);

            await transaction.CommitAsync();

            _logger.LogInformation("Payment {PaymentId} updated to status {Status}. KNET Result: {Result}",
                payment.PaymentId, payment.Status, gatewayResponse.Result);

            return new KnetPaymentResponseDto
            {
                Success = isCaptured,
                PaymentId = payment.PaymentId.ToString(),
                KnetPaymentId = gatewayResponse.PaymentId,
                Status = payment.Status.ToString(),
                TransactionReference = payment.ReceiptNumber,
                ErrorMessage = isCaptured ? null : $"{gatewayResponse.Result}: {gatewayResponse.Error} {gatewayResponse.ErrorText}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling KNET response for TrackId: {TrackId}", trackId);
            return new KnetPaymentResponseDto { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// TEST MODE: Simulates KNET payment without calling the KNET gateway
    /// </summary>
    private async Task<KnetPaymentResponseDto> InitiateTestPaymentAsync(
        KnetPaymentRequestDto request,
        string trackId,
        int leaseId,
        Property building,
        bool hasMismatch,
        string? mismatchDetails)
    {
        _logger.LogInformation("TEST MODE: Simulating KNET payment for Invoice {InvoiceId}, Amount: {Amount} KWD, Building: {Building}",
            request.InvoiceId, request.Amount, building.PropertyName);

        using var transaction = await _context.Database.BeginTransactionAsync();
        var now = DateTime.UtcNow;
        var payment = new Payment
        {
            InvoiceId = request.InvoiceId,
            LeaseId = leaseId,
            TenantId = request.TenantId,
            PropertyId = request.PropertyId,
            AmountPaid = request.Amount,
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
        var testPaymentUrl = $"{testBaseUri.Scheme}://{testBaseUri.Authority}/TenantPortal/TestPayment?paymentId={payment.PaymentId}&knetPaymentId={payment.CheckoutPaymentId}&amount={request.Amount}&invoiceId={request.InvoiceId}";

        // Create idempotency record (with the test redirect URL cached for resume)
        if (!string.IsNullOrEmpty(request.IdempotencyKey))
        {
            _context.PaymentIdempotencies.Add(new PaymentIdempotency
            {
                IdempotencyKey = request.IdempotencyKey,
                PaymentId = payment.PaymentId,
                InvoiceId = request.InvoiceId,
                LeaseId = leaseId,
                Amount = request.Amount,
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
                Amount = request.Amount,
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

            using var transaction = await _context.Database.BeginTransactionAsync();
            var completedAt = DateTime.UtcNow;
            payment.ModifiedDate = completedAt;

            if (success)
            {
                payment.Status = PaymentStatus.Completed;
                payment.ApprovedDate = completedAt;
                // Receipt number is issued at the moment of capture.
                payment.ReceiptNumber = $"RCPT-{payment.PaymentId:D8}";
                payment.Notes = (payment.Notes ?? "") + $"\nPayment completed successfully at {completedAt:yyyy-MM-dd HH:mm:ss} UTC";

                if (payment.InvoiceId.HasValue)
                {
                    var invoice = await _invoiceRepository.GetByIdAsync(payment.InvoiceId.Value);
                    if (invoice != null)
                    {
                        invoice.PaidAmount += payment.AmountPaid;
                        invoice.Status = invoice.PaidAmount >= invoice.TotalAmount ? "Paid" : "Partially Paid";
                        await _invoiceRepository.UpdateAsync(invoice);
                    }
                }
            }
            else
            {
                payment.Status = PaymentStatus.Failed;
                payment.Notes = (payment.Notes ?? "") + $"\nPayment failed at {completedAt:yyyy-MM-dd HH:mm:ss} UTC";
            }

            await _paymentRepository.UpdateAsync(payment);
            await SyncIdempotencyStatusAsync(payment);
            await transaction.CommitAsync();

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
            return new KnetPaymentResponseDto { Success = false, ErrorMessage = ex.Message };
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
