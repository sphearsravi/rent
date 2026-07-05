using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Application.DTOs;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.Controllers.Api;

[ApiController]
[Route("api/payments/bank")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class BankTransferApiController : ControllerBase
{
    private readonly IBankTransferService _bankTransferService;
    private readonly IFileStorageService _fileStorageService;
    private readonly IPaymentEligibilityService _eligibility;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<BankTransferApiController> _logger;

    public BankTransferApiController(
        IBankTransferService bankTransferService,
        IFileStorageService fileStorageService,
        IPaymentEligibilityService eligibility,
        ApplicationDbContext context,
        ILogger<BankTransferApiController> logger)
    {
        _bankTransferService = bankTransferService;
        _fileStorageService = fileStorageService;
        _eligibility = eligibility;
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Tenant ID from the JWT `tenantId` claim. The Flutter app authenticates
    /// with a bearer token, not a cookie session — the previous
    /// Session.GetInt32("TenantId") always failed for it.
    /// </summary>
    private int? ClaimTenantId =>
        int.TryParse(User.FindFirst("tenantId")?.Value, out var id) ? id : null;

    /// <summary>
    /// Submit bank transfer (Tenant portal - uses session authentication)
    /// POST: api/payments/bank/upload
    /// </summary>
    [HttpPost("upload")]
    public async Task<ActionResult<BankTransferResponseDto>> SubmitBankTransfer([FromForm] BankTransferSubmitDto submitDto)
    {
        try
        {
            var tenantId = ClaimTenantId;
            if (tenantId == null)
            {
                return Unauthorized(new { success = false, message = "Unauthorized. Please login." });
            }

            // Server-authoritative: tenant + property are derived from the
            // invoice's lease, NOT from the client. The Flutter form doesn't
            // (and shouldn't) send PropertyId — sending 0 previously caused an
            // FK violation on FK_Payments_Properties.
            if (submitDto.InvoiceId is null or 0)
                return BadRequest(new { success = false, message = "InvoiceId is required." });

            var invoice = await _context.Invoices
                .Include(i => i.Lease).ThenInclude(l => l.Unit)
                .FirstOrDefaultAsync(i => i.InvoiceId == submitDto.InvoiceId.Value);

            if (invoice == null)
                return BadRequest(new { success = false, message = "Invoice not found." });

            if (invoice.TenantId != tenantId.Value)
                return Forbid();

            submitDto.TenantId = tenantId.Value;
            submitDto.PropertyId = invoice.Lease?.Unit?.PropertyId ?? 0;
            if (submitDto.PropertyId == 0)
                return BadRequest(new { success = false, message = "Invoice is not linked to a property." });

            // Sprint 24 — block submission if a chronologically older
            // invoice is still outstanding. Same rule that gates K-NET
            // and cash. Audit-logged via the existing PaymentAudits row.
            var eligibility = await _eligibility.CheckAsync(invoice.InvoiceId);
            if (!eligibility.IsEligible)
            {
                _logger.LogWarning(
                    "Bank transfer blocked — tenant {TenantId} tried invoice {InvoiceId}, must pay {OldestId} first",
                    tenantId.Value, invoice.InvoiceId, eligibility.OldestUnpaidInvoiceId);
                return Conflict(new
                {
                    success = false,
                    message = eligibility.Reason,
                    oldestInvoiceId = eligibility.OldestUnpaidInvoiceId,
                    oldestBalance = eligibility.OldestBalance,
                });
            }

            var result = await _bankTransferService.SubmitBankTransferAsync(submitDto, $"tenant:{tenantId}");

            // Audit the submission (parity with cash "CashRecorded"; approve /
            // reject are audited in PaymentApprovalController).
            _context.PaymentAudits.Add(new PaymentAudit
            {
                PaymentId = result.PaymentId,
                AuditType = "BankTransferSubmitted",
                AuditTimestamp = DateTime.UtcNow,
                Amount = result.Amount,
                AuditedBy = $"tenant:{tenantId}",
                IsMismatch = false,
                IsResolved = false,
                Notes = $"Bank transfer submitted with proof, pending verification"
            });
            await _context.SaveChangesAsync();

            _logger.LogInformation("Bank transfer submitted by tenant {TenantId}", tenantId);

            return Ok(new
            {
                success = true,
                message = "Bank transfer submitted successfully. Awaiting admin verification.",
                data = result
            });
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Invalid bank transfer submission: {Message}", ex.Message);
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting bank transfer");
            return StatusCode(500, new { success = false, message = "An error occurred while submitting the bank transfer. " + ex.Message });
        }
    }

    /// <summary>
    /// Get bank transfer by ID (Tenant or Admin)
    /// GET: api/payments/bank/{id}
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<BankTransferResponseDto>> GetBankTransfer(int id)
    {
        try
        {
            var tenantId = ClaimTenantId;
            if (tenantId == null)
            {
                return Unauthorized(new { success = false, message = "Unauthorized" });
            }

            var result = await _bankTransferService.GetBankTransferByIdAsync(id);

            if (result == null)
                return NotFound(new { success = false, message = "Bank transfer not found" });

            // Verify tenant can only see their own transfers
            if (result.TenantId != tenantId.Value)
            {
                return Forbid();
            }

            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving bank transfer {Id}", id);
            return StatusCode(500, new { success = false, message = "An error occurred while retrieving the bank transfer" });
        }
    }

    /// <summary>
    /// Get bank transfers by current logged-in tenant
    /// GET: api/payments/bank/tenant/current
    /// </summary>
    [HttpGet("tenant/current")]
    public async Task<ActionResult<IEnumerable<BankTransferResponseDto>>> GetMyBankTransfers()
    {
        try
        {
            var tenantId = ClaimTenantId;
            if (tenantId == null)
            {
                return Unauthorized(new { success = false, message = "Unauthorized" });
            }

            var result = await _bankTransferService.GetBankTransfersByTenantAsync(tenantId.Value);
            return Ok(new { success = true, data = result, count = result.Count() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving bank transfers for tenant");
            return StatusCode(500, new { success = false, message = "An error occurred while retrieving bank transfers" });
        }
    }

    /// <summary>
    /// Download bank transfer proof file
    /// GET: api/payments/bank/{id}/proof
    /// </summary>
    [HttpGet("{id}/proof")]
    public async Task<ActionResult> DownloadProofFile(int id)
    {
        try
        {
            var tenantId = ClaimTenantId;
            if (tenantId == null)
            {
                return Unauthorized();
            }

            // Ownership check — a tenant may only download their own proof.
            // (Admin proof access goes through the cookie-authed PaymentApproval
            // screen, not this JWT endpoint.)
            var transfer = await _bankTransferService.GetBankTransferByIdAsync(id);
            if (transfer == null)
                return NotFound(new { success = false, message = "Bank transfer not found" });
            if (transfer.TenantId != tenantId.Value)
                return Forbid();

            var filePath = await _bankTransferService.GetBankTransferProofFilePathAsync(id);
            var fileBytes = await _fileStorageService.GetFileAsync(filePath);
            var fileName = Path.GetFileName(filePath);

            return File(fileBytes, "application/octet-stream", fileName);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning("Proof file not found for bank transfer {Id}: {Message}", id, ex.Message);
            return NotFound(new { success = false, message = "Proof file not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading proof file for bank transfer {Id}", id);
            return StatusCode(500, new { success = false, message = "An error occurred while downloading the file" });
        }
    }
}
