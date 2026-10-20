using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Application.DTOs;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Core.Enums;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.Controllers.Api;

/// <summary>
/// Tenant-initiated cash payment request. The tenant can't actually move money
/// from the app, so this records a cash payment as PendingVerification and puts
/// it in the office's PaymentApproval queue — staff confirm it when the tenant
/// pays at the counter (the same two-person control as bank transfer: the
/// tenant claims it, a different person verifies). Mirrors BankTransferApiController.
/// </summary>
[ApiController]
[Route("api/payments/cash")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CashRequestApiController : ControllerBase
{
    private readonly ICashPaymentService _cashPaymentService;
    private readonly IPaymentEligibilityService _eligibility;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CashRequestApiController> _logger;

    public CashRequestApiController(
        ICashPaymentService cashPaymentService,
        IPaymentEligibilityService eligibility,
        ApplicationDbContext context,
        ILogger<CashRequestApiController> logger)
    {
        _cashPaymentService = cashPaymentService;
        _eligibility = eligibility;
        _context = context;
        _logger = logger;
    }

    private int? ClaimTenantId =>
        int.TryParse(User.FindFirst("tenantId")?.Value, out var id) ? id : null;

    public class CashRequestDto
    {
        public int? InvoiceId { get; set; }
        /// <summary>
        /// Optional. The amount the tenant intends to hand over at the counter.
        /// Omitted = the full outstanding balance. Must be &gt; 0 and ≤ balance;
        /// the server never trusts it beyond that range.
        /// </summary>
        public decimal? Amount { get; set; }
        public string? Notes { get; set; }
    }

    /// <summary>POST: api/payments/cash/request</summary>
    [HttpPost("request")]
    public async Task<IActionResult> RequestCash([FromBody] CashRequestDto request)
    {
        try
        {
            var tenantId = ClaimTenantId;
            if (tenantId == null)
                return Unauthorized(new { success = false, message = "Unauthorized. Please login." });

            if (request.InvoiceId is null or 0)
                return BadRequest(new { success = false, message = "InvoiceId is required." });

            var invoice = await _context.Invoices
                .Include(i => i.Lease).ThenInclude(l => l!.Unit)
                .FirstOrDefaultAsync(i => i.InvoiceId == request.InvoiceId.Value);

            if (invoice == null)
                return BadRequest(new { success = false, message = "Invoice not found." });

            // Server-authoritative ownership — never trust the client's tenant.
            if (invoice.TenantId != tenantId.Value)
                return Forbid();

            if (string.Equals(invoice.Status, InvoiceStatuses.Paid, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, message = "This invoice is already paid." });

            var propertyId = invoice.Lease?.Unit?.PropertyId ?? 0;
            if (propertyId == 0)
                return BadRequest(new { success = false, message = "Invoice is not linked to a property." });

            // Same oldest-unpaid-first rule that gates KNET + bank transfer.
            var eligibility = await _eligibility.CheckAsync(invoice.InvoiceId);
            if (!eligibility.IsEligible)
            {
                return Conflict(new
                {
                    success = false,
                    message = eligibility.Reason,
                    oldestInvoiceId = eligibility.OldestUnpaidInvoiceId,
                    oldestBalance = eligibility.OldestBalance,
                });
            }

            // Don't let a tenant stack duplicate cash requests for one invoice —
            // one pending (unverified) cash record at a time.
            var alreadyPending = await _context.Payments.AsNoTracking().AnyAsync(p =>
                p.InvoiceId == invoice.InvoiceId
                && p.PaymentType == PaymentType.Cash
                && p.Status == PaymentStatus.PendingVerification);
            if (alreadyPending)
                return Conflict(new { success = false, message = "A cash payment for this invoice is already awaiting office confirmation." });

            // The outstanding balance is computed server-side. The tenant may
            // declare a smaller (partial) amount they'll bring to the counter,
            // but never more than the balance and never zero/negative.
            var balance = decimal.Round(invoice.TotalAmount - invoice.PaidAmount, 3);
            if (balance <= 0)
                return BadRequest(new { success = false, message = "Nothing outstanding on this invoice." });

            var amount = balance;
            if (request.Amount.HasValue)
            {
                amount = decimal.Round(request.Amount.Value, 3);
                if (amount <= 0 || amount > balance)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = $"Amount must be between 0.001 and the outstanding balance of KWD {balance:0.000}.",
                        balance,
                    });
                }
            }
            var isPartial = amount < balance;

            var baseNote = isPartial
                ? $"Cash payment requested by tenant via the app — PARTIAL: KWD {amount:0.000} of KWD {balance:0.000} outstanding — pending office confirmation."
                : "Cash payment requested by tenant via the app — pending office confirmation.";

            var dto = new CashPaymentDto
            {
                InvoiceId = invoice.InvoiceId,
                TenantId = tenantId.Value,
                PropertyId = propertyId,
                Amount = amount,
                PaymentDate = DateTime.UtcNow,
                Notes = string.IsNullOrWhiteSpace(request.Notes)
                    ? baseNote
                    : $"{baseNote}\nTenant note: {request.Notes.Trim()}",
            };

            var result = await _cashPaymentService.RecordCashPaymentAsync(dto, $"tenant-request:{tenantId}");

            _context.PaymentAudits.Add(new PaymentAudit
            {
                PaymentId = result.PaymentId,
                AuditType = "CashRequestedByTenant",
                AuditTimestamp = DateTime.UtcNow,
                Amount = result.Amount,
                AuditedBy = $"tenant:{tenantId}",
                IsMismatch = false,
                IsResolved = false,
                Notes = isPartial
                    ? $"Tenant requested to pay KWD {amount:0.000} cash (partial of KWD {balance:0.000}), receipt {result.ReceiptNumber}, pending office confirmation"
                    : $"Tenant requested to pay cash, receipt {result.ReceiptNumber}, pending office confirmation",
            });
            await _context.SaveChangesAsync();

            _logger.LogInformation("Cash payment requested by tenant {TenantId} for invoice {InvoiceId} (KWD {Amount}, partial={Partial})",
                tenantId, invoice.InvoiceId, amount, isPartial);

            // The receipt number is deliberately NOT returned here: no money has
            // changed hands yet. The tenant gets the receipt (and the PDF) once
            // the office confirms the payment.
            return Ok(new
            {
                success = true,
                message = "The office has been notified. Please pay at the counter; staff will confirm your payment.",
                data = new
                {
                    paymentId = result.PaymentId,
                    amount = result.Amount,
                    balance,
                    isPartial,
                    status = result.Status,
                    paymentDate = result.PaymentDate,
                },
            });
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Invalid cash request: {Message}", ex.Message);
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating tenant cash request");
            return StatusCode(500, new { success = false, message = "An error occurred while requesting the cash payment. " + ex.Message });
        }
    }
}
