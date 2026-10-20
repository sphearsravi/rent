using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;
using RentCollection.Web.DTOs.Api;

namespace RentCollection.Web.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = "Bearer")]
public class TenantController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TenantController> _logger;

    public TenantController(
        ApplicationDbContext context,
        ILogger<TenantController> logger)
    {
        _context = context;
        _logger = logger;
    }

    private bool IsAuthorizedForTenant(int tenantId)
    {
        var claim = User.FindFirst("tenantId")?.Value;
        return int.TryParse(claim, out var claimTenantId) && claimTenantId == tenantId;
    }

    [HttpPost("getinfo")]
    public async Task<ActionResult<CustomResponseDto<TenantInfoData>>> GetInfo([FromBody] GetInfoRequest request)
    {
        try
        {
            if (request?.Input == null || request.Input.Tenantid <= 0)
            {
                return Ok(CustomResponseDto<TenantInfoData>.ErrorResponse("Invalid tenant ID", 400));
            }
            if (!IsAuthorizedForTenant(request.Input.Tenantid))
            {
                return Ok(CustomResponseDto<TenantInfoData>.ErrorResponse("Forbidden", 403));
            }

            var tenant = await _context.Tenants
                .Include(t => t.Company)
                .FirstOrDefaultAsync(t => t.TenantId == request.Input.Tenantid);

            if (tenant == null)
            {
                return Ok(CustomResponseDto<TenantInfoData>.ErrorResponse("Tenant not found", 404));
            }

            var tenantInfo = new TenantInfoData
            {
                TenantId = tenant.TenantId,
                FirstName = tenant.FirstName,
                LastName = tenant.LastName,
                FullName = $"{tenant.FirstName} {tenant.LastName}",
                Email = tenant.Email,
                Phone = tenant.Phone,
                Mobile = tenant.Phone,
                PACINumber = tenant.PACINumber,
                CompanyId = tenant.CompanyId,
                CreatedDate = tenant.CreatedDate
            };

            return Ok(CustomResponseDto<TenantInfoData>.SuccessResponse(tenantInfo, "Tenant information retrieved successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tenant information");
            return Ok(CustomResponseDto<TenantInfoData>.ErrorResponse("An error occurred while retrieving tenant information", 500));
        }
    }

    [HttpPost("getpropertybytenantid")]
    public async Task<ActionResult<CustomResponseDto<List<PropertyData>>>> GetPropertyByTenantId([FromBody] GetPropertyByTenantIdRequest request)
    {
        try
        {
            if (request?.Input == null || request.Input.Tenantid <= 0)
            {
                return Ok(CustomResponseDto<List<PropertyData>>.ErrorResponse("Invalid tenant ID", 400));
            }
            if (!IsAuthorizedForTenant(request.Input.Tenantid))
            {
                return Ok(CustomResponseDto<List<PropertyData>>.ErrorResponse("Forbidden", 403));
            }

            // Get properties for this tenant through their leases
            var properties = await _context.Leases
                .Where(l => l.TenantId == request.Input.Tenantid && l.Status == "Active")
                .Include(l => l.Unit)
                    .ThenInclude(u => u.Property)
                .Select(l => new PropertyData
                {
                    PropertyId = l.Unit.Property.PropertyId,
                    PropertyName = l.Unit.Property.PropertyName,
                    PropertyNumber = l.Unit.Property.BuildingNumber ?? "",
                    BuildingName = l.Unit.Property.PropertyName,
                    Area = l.Unit.Property.Area,
                    Block = l.Unit.Property.Block,
                    Street = l.Unit.Property.Street ?? "",
                    Address = l.Unit.Property.Address
                })
                .Distinct()
                .ToListAsync();

            return Ok(CustomResponseDto<List<PropertyData>>.SuccessResponse(properties, "Properties retrieved successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving properties for tenant");
            return Ok(CustomResponseDto<List<PropertyData>>.ErrorResponse("An error occurred while retrieving properties", 500));
        }
    }

    [HttpPost("getpendingrentdata")]
    public async Task<ActionResult<CustomResponseDto<List<PendingRentData>>>> GetPendingRentData([FromBody] GetPendingRentDataRequest request)
    {
        try
        {
            if (request?.Input == null || request.Input.Tenantid <= 0)
            {
                return Ok(CustomResponseDto<List<PendingRentData>>.ErrorResponse("Invalid tenant ID", 400));
            }
            if (!IsAuthorizedForTenant(request.Input.Tenantid))
            {
                return Ok(CustomResponseDto<List<PendingRentData>>.ErrorResponse("Forbidden", 403));
            }

            // Get pending invoices for this tenant
            var pendingRents = await _context.Invoices
                .Where(i => i.TenantId == request.Input.Tenantid &&
                           (i.Status == "Pending" || i.Status == "Overdue"))
                .Include(i => i.Lease)
                    .ThenInclude(l => l.Unit)
                        .ThenInclude(u => u.Property)
                .Select(i => new PendingRentData
                {
                    TenantRentHistoryId = i.InvoiceId,
                    TenantId = i.TenantId,
                    PropertyId = i.Lease.Unit.Property.PropertyId,
                    PropertyName = i.Lease.Unit.Property.PropertyName,
                    PropertyNumber = i.Lease.Unit.Property.BuildingNumber ?? "",
                    RentDate = i.InvoiceDate,
                    RentAmount = i.TotalAmount,
                    NetAmount = i.TotalAmount - i.PaidAmount,
                    Currency = "KWD",
                    Status = i.Status
                })
                .ToListAsync();

            return Ok(CustomResponseDto<List<PendingRentData>>.SuccessResponse(pendingRents, "Pending rent data retrieved successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving pending rent data");
            return Ok(CustomResponseDto<List<PendingRentData>>.ErrorResponse("An error occurred while retrieving pending rent data", 500));
        }
    }

    [HttpPost("getpaidrentdata")]
    public async Task<ActionResult<CustomResponseDto<List<PaidRentData>>>> GetPaidRentData([FromBody] GetPaidRentDataRequest request)
    {
        try
        {
            if (request?.Input == null || request.Input.Tenantid <= 0)
            {
                return Ok(CustomResponseDto<List<PaidRentData>>.ErrorResponse("Invalid tenant ID", 400));
            }
            if (!IsAuthorizedForTenant(request.Input.Tenantid))
            {
                return Ok(CustomResponseDto<List<PaidRentData>>.ErrorResponse("Forbidden", 403));
            }

            var lastid = request.Input.Lastid > 0 ? request.Input.Lastid : 0;
            var count = request.Input.Count > 0 ? request.Input.Count : 10;

            // Get paid invoices for this tenant with pagination
            var paidRents = await _context.Invoices
                .Where(i => i.TenantId == request.Input.Tenantid &&
                           i.Status == "Paid" &&
                           i.InvoiceId > lastid)
                .Include(i => i.Lease)
                    .ThenInclude(l => l.Unit)
                        .ThenInclude(u => u.Property)
                .OrderBy(i => i.InvoiceId)
                .Take(count)
                .Select(i => new PaidRentData
                {
                    TenantRentHistoryId = i.InvoiceId,
                    TenantId = i.TenantId,
                    PropertyId = i.Lease.Unit.Property.PropertyId,
                    PropertyName = i.Lease.Unit.Property.PropertyName,
                    PropertyNumber = i.Lease.Unit.Property.BuildingNumber ?? "",
                    RentDate = i.InvoiceDate,
                    PaidDate = i.CreatedDate,
                    RentAmount = i.TotalAmount,
                    PaidAmount = i.PaidAmount,
                    Currency = "KWD",
                    PaymentReference = "",
                    PaymentId = i.InvoiceId.ToString()
                })
                .ToListAsync();

            return Ok(CustomResponseDto<List<PaidRentData>>.SuccessResponse(paidRents, "Paid rent data retrieved successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving paid rent data");
            return Ok(CustomResponseDto<List<PaidRentData>>.ErrorResponse("An error occurred while retrieving paid rent data", 500));
        }
    }

    [HttpPost("getreceipt")]
    public async Task<ActionResult<CustomResponseDto<ReceiptData>>> GetReceipt([FromBody] GetReceiptDataRequest request)
    {
        try
        {
            if (request?.Input == null)
            {
                return Ok(CustomResponseDto<ReceiptData>.ErrorResponse("Invalid request", 400));
            }

            // Find payment by tracking ID or invoice ID
            Payment? payment = null;

            if (!string.IsNullOrEmpty(request.Input.TrackId))
            {
                payment = await _context.Payments
                    .Include(p => p.Tenant)
                    .Include(p => p.Lease)
                        .ThenInclude(l => l.Unit)
                            .ThenInclude(u => u.Property)
                    .FirstOrDefaultAsync(p => p.TransactionReference == request.Input.TrackId);
            }
            else if (request.Input.TenantId > 0)
            {
                if (!IsAuthorizedForTenant(request.Input.TenantId))
                {
                    return Ok(CustomResponseDto<ReceiptData>.ErrorResponse("Forbidden", 403));
                }
                payment = await _context.Payments
                    .Include(p => p.Tenant)
                    .Include(p => p.Lease)
                        .ThenInclude(l => l.Unit)
                            .ThenInclude(u => u.Property)
                    .Where(p => p.TenantId == request.Input.TenantId)
                    .OrderByDescending(p => p.PaymentDate)
                    .FirstOrDefaultAsync();
            }

            // When a TrackId is used, ensure the resolved payment belongs to the caller.
            if (payment != null && !IsAuthorizedForTenant(payment.TenantId))
            {
                return Ok(CustomResponseDto<ReceiptData>.ErrorResponse("Forbidden", 403));
            }

            if (payment == null)
            {
                return Ok(CustomResponseDto<ReceiptData>.ErrorResponse("Payment not found", 404));
            }

            var receiptData = new ReceiptData
            {
                PaymentId = payment.PaymentId,
                TenantId = payment.TenantId,
                TenantName = $"{payment.Tenant.FirstName} {payment.Tenant.LastName}",
                PropertyName = payment.Lease.Unit.Property.PropertyName,
                Amount = payment.AmountPaid,
                Currency = "KWD",
                PaymentDate = payment.PaymentDate,
                PaymentReference = payment.TransactionReference ?? "",
                TrackId = payment.TransactionReference ?? ""
            };

            return Ok(CustomResponseDto<ReceiptData>.SuccessResponse(receiptData, "Receipt retrieved successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving receipt");
            return Ok(CustomResponseDto<ReceiptData>.ErrorResponse("An error occurred while retrieving receipt", 500));
        }
    }

    [HttpPost("changepassword")]
    public async Task<ActionResult<CustomResponseDto<string>>> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        try
        {
            if (request?.Input == null || request.Input.Tenantid <= 0)
            {
                return Ok(CustomResponseDto<string>.ErrorResponse("Invalid request", 400));
            }
            if (!IsAuthorizedForTenant(request.Input.Tenantid))
            {
                return Ok(CustomResponseDto<string>.ErrorResponse("Forbidden", 403));
            }

            if (string.IsNullOrEmpty(request.Input.Oldpassword) || string.IsNullOrEmpty(request.Input.Newpassword))
            {
                return Ok(CustomResponseDto<string>.ErrorResponse("Old password and new password are required", 400));
            }

            var tenant = await _context.Tenants.FindAsync(request.Input.Tenantid);

            if (tenant == null)
            {
                return Ok(CustomResponseDto<string>.ErrorResponse("Tenant not found", 404));
            }

            // SECURITY (audit H4): verify and store with BCrypt, never plaintext.
            // The old code compared/stored the raw password, which both exposed
            // credentials on any DB read and overwrote the BCrypt hash the real
            // login (/api/tenant/login) verifies against. Guard the verify against
            // any legacy non-hash value.
            bool oldOk;
            try
            {
                oldOk = !string.IsNullOrEmpty(tenant.Password)
                    && BCrypt.Net.BCrypt.Verify(request.Input.Oldpassword, tenant.Password);
            }
            catch { oldOk = false; }

            if (!oldOk)
            {
                return Ok(CustomResponseDto<string>.ErrorResponse("Old password is incorrect", 401));
            }

            if (request.Input.Newpassword.Length < 8)
            {
                return Ok(CustomResponseDto<string>.ErrorResponse("New password must be at least 8 characters", 400));
            }

            // Update password (BCrypt hash)
            tenant.Password = BCrypt.Net.BCrypt.HashPassword(request.Input.Newpassword);
            tenant.ModifiedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(CustomResponseDto<string>.SuccessResponse("Password changed successfully", "Password changed successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password");
            return Ok(CustomResponseDto<string>.ErrorResponse("An error occurred while changing password", 500));
        }
    }

    [HttpPost("forgotpassword")]
    public async Task<ActionResult<CustomResponseDto<string>>> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        try
        {
            if (request?.Input == null || string.IsNullOrEmpty(request.Input.Civilid))
            {
                return Ok(CustomResponseDto<string>.ErrorResponse("Civil ID is required", 400));
            }

            // SECURITY (audit M6): do NOT reveal whether an account exists or leak
            // its email — that turned this into a Civil-ID/email enumeration oracle.
            // Always return the same generic message regardless of any match, and
            // never echo the tenant's email. (This flow is a stub — no reset token
            // is issued here — so there is nothing to disclose.)
            _logger.LogInformation("Password reset requested (identifier masked).");

            return Ok(CustomResponseDto<string>.SuccessResponse(
                "If an account matches, password reset instructions will be sent.",
                "Password reset request received"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing forgot password request");
            return Ok(CustomResponseDto<string>.ErrorResponse("An error occurred while processing your request", 500));
        }
    }
}
