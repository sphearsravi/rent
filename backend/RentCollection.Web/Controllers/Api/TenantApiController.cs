using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Application.Interfaces;
using RentCollection.Infrastructure.Data;
using RentCollection.Core.Entities;
using System.ComponentModel.DataAnnotations;

namespace RentCollection.Web.Controllers.Api;

[ApiController]
[Route("api/tenant")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class TenantApiController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IJwtTokenService _jwtTokenService;

    private readonly RentCollection.Web.Services.ILoginThrottle _loginThrottle;

    public TenantApiController(ApplicationDbContext context, IJwtTokenService jwtTokenService,
        RentCollection.Web.Services.ILoginThrottle loginThrottle)
    {
        _context = context;
        _jwtTokenService = jwtTokenService;
        _loginThrottle = loginThrottle;
    }

    /// <summary>
    /// Returns true when the JWT claim "tenantId" matches the route tenantId.
    /// All authenticated actions must call this before reading or mutating
    /// data keyed on tenantId, to prevent IDOR.
    /// </summary>
    private bool IsAuthorizedForTenant(int tenantId)
    {
        var claim = User.FindFirst("tenantId")?.Value;
        return int.TryParse(claim, out var claimTenantId) && claimTenantId == tenantId;
    }

    #region Authentication APIs

    /// <summary>
    /// Login tenant with Civil ID and Password. Civil ID is the tenant's
    /// personal Kuwait Government ID (stored as `Tenant.GovernmentId`).
    ///
    /// The DTO still accepts `PACINumber` as a deprecated alias so older app
    /// builds still work during the rollout window — both fields are
    /// matched against `GovernmentId`. PACI (the property identifier) is
    /// never a login identifier.
    ///
    /// POST: api/tenant/login
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] TenantLoginRequest request)
    {
        var civilId = !string.IsNullOrWhiteSpace(request.CivilId)
            ? request.CivilId
            : request.PACINumber;   // deprecated alias, treated as Civil ID

        if (string.IsNullOrWhiteSpace(civilId) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { success = false, message = "Civil ID and Password are required" });
        }

        // SECURITY (audit H2): per-account brute-force lockout.
        if (_loginThrottle.IsLockedOut(civilId, out _))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { success = false, message = "Too many failed attempts. Please try again later." });
        }

        // Login is anonymous — the company-scope HasQueryFilter would
        // otherwise hide every tenant (no claim = no CompanyId match).
        // IgnoreQueryFilters bypasses both the company scope AND the
        // soft-delete filter; deleted tenants are excluded explicitly.
        var tenant = await _context.Tenants
            .IgnoreQueryFilters()
            .Include(t => t.Company)
            .FirstOrDefaultAsync(t =>
                t.Status == "Active"
                && !t.IsDeleted
                && t.GovernmentId == civilId);

        if (tenant == null || !BCrypt.Net.BCrypt.Verify(request.Password, tenant.Password))
        {
            _loginThrottle.RecordFailure(civilId);
            return Unauthorized(new { success = false, message = "Invalid credentials" });
        }

        _loginThrottle.Reset(civilId);
        var token = _jwtTokenService.GenerateToken(tenant.TenantId, tenant.Email ?? string.Empty,
            RentCollection.Web.Services.JwtTokenService.ComputeSecurityStamp(tenant.Password),
            longLived: request.RememberMe);

        return Ok(new
        {
            success = true,
            message = "Login successful",
            // mustChangePassword surfaces the admin-issued-temp-password flag
            // so the client can route straight to a change-password screen.
            // Tenants who set their own password via the change endpoint
            // get the flag cleared automatically.
            mustChangePassword = tenant.MustChangePasswordOnNextLogin,
            data = new
            {
                tenantId = tenant.TenantId,
                token,
                firstName = tenant.FirstName,
                lastName = tenant.LastName,
                email = tenant.Email,
                phone = tenant.Phone,
                civilId = tenant.GovernmentId,
                paciNumber = tenant.PACINumber,  // property reference, not a login identifier
                company = tenant.Company?.CompanyName,
                mustChangePassword = tenant.MustChangePasswordOnNextLogin,
            }
        });
    }

    /// <summary>
    /// Refresh the JWT. The caller passes their current (valid, non-expired)
    /// JWT; the server validates the tenant is still active and issues a new
    /// token with a fresh expiry. Lets the Flutter app silently renew on
    /// every cold start so the tenant doesn't bounce to login.
    ///
    /// This is NOT a refresh-token endpoint in the full sense — there is no
    /// separate long-lived refresh token. It's a same-session renewal: only
    /// works while the existing JWT is still valid. When the JWT is fully
    /// expired the user must log in again. A real refresh-token table is a
    /// later improvement.
    ///
    /// POST: api/tenant/refresh
    /// Rate-limited (Sprint 4 A5): 6 calls/minute per IP via the
    /// "refresh" policy in Program.cs. Abuse of this endpoint was the main
    /// reason we couldn't drop access-token TTL earlier.
    /// </summary>
    [HttpPost("refresh")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("refresh")]
    public async Task<IActionResult> Refresh()
    {
        var claim = User.FindFirst("tenantId")?.Value;
        if (!int.TryParse(claim, out var tenantId))
            return Unauthorized();

        var tenant = await _context.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Status == "Active");
        if (tenant == null)
            return Unauthorized();

        // Preserve the "remember me" TTL across refreshes: a long-lived session
        // keeps getting long-lived tokens, so the app stays signed in for the
        // full 30-day window instead of silently dropping to 60 minutes.
        var remembered = User.FindFirst("rm")?.Value == "1";
        var token = _jwtTokenService.GenerateToken(tenant.TenantId, tenant.Email ?? string.Empty,
            RentCollection.Web.Services.JwtTokenService.ComputeSecurityStamp(tenant.Password),
            longLived: remembered);
        return Ok(new { success = true, data = new { token } });
    }

    /// <summary>
    /// Change tenant password
    /// POST: api/tenant/change-password
    /// </summary>
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] TenantApiChangePasswordRequest request)
    {
        if (!IsAuthorizedForTenant(request.TenantId))
            return Forbid();

        var tenant = await _context.Tenants.FindAsync(request.TenantId);
        if (tenant == null)
        {
            return NotFound(new { success = false, message = "Tenant not found" });
        }

        if (!BCrypt.Net.BCrypt.Verify(request.OldPassword, tenant.Password))
        {
            return BadRequest(new { success = false, message = "Current password is incorrect" });
        }

        // SECURITY (audit M3): enforce the shared password policy.
        var policyError = RentCollection.Web.Services.PasswordPolicy.Validate(request.NewPassword);
        if (policyError != null)
            return BadRequest(new { success = false, message = policyError });

        tenant.Password = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        // Tenant has picked their own password — clear the force-change flag
        // so subsequent logins don't keep nagging them.
        tenant.MustChangePasswordOnNextLogin = false;
        tenant.ModifiedDate = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // SECURITY (audit M2): the password change rotates the token security stamp,
        // which invalidates the caller's CURRENT token. Return a fresh token so the
        // legitimate user isn't logged out mid-session.
        var newToken = _jwtTokenService.GenerateToken(tenant.TenantId, tenant.Email ?? string.Empty,
            RentCollection.Web.Services.JwtTokenService.ComputeSecurityStamp(tenant.Password),
            longLived: User.FindFirst("rm")?.Value == "1");

        return Ok(new { success = true, message = "Password changed successfully", data = new { token = newToken } });
    }

    #endregion

    #region Profile APIs

    /// <summary>
    /// Get tenant profile details
    /// GET: api/tenant/{tenantId}/profile
    /// </summary>
    [HttpGet("{tenantId}/profile")]
    public async Task<IActionResult> GetProfile(int tenantId)
    {
        if (!IsAuthorizedForTenant(tenantId)) return Forbid();
        var tenant = await _context.Tenants
            .AsNoTracking()
            .Include(t => t.Company)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId);

        if (tenant == null)
        {
            return NotFound(new { success = false, message = "Tenant not found" });
        }

        return Ok(new
        {
            success = true,
            data = new
            {
                tenantId = tenant.TenantId,
                firstName = tenant.FirstName,
                lastName = tenant.LastName,
                email = tenant.Email,
                phone = tenant.Phone,
                paciNumber = tenant.PACINumber,
                governmentId = tenant.GovernmentId,
                dateOfBirth = tenant.DateOfBirth,
                // nationality = tenant.Nationality, // Property doesn't exist
                company = tenant.Company?.CompanyName,
                status = tenant.Status
                // emergencyContactName = tenant.EmergencyContactName, // Property doesn't exist
                // emergencyContactPhone = tenant.EmergencyContactPhone // Property doesn't exist
            }
        });
    }

    /// <summary>
    /// Update tenant profile
    /// PUT: api/tenant/{tenantId}/profile
    /// </summary>
    [HttpPut("{tenantId}/profile")]
    public async Task<IActionResult> UpdateProfile(int tenantId, [FromBody] UpdateProfileRequest request)
    {
        if (!IsAuthorizedForTenant(tenantId)) return Forbid();
        var tenant = await _context.Tenants.FindAsync(tenantId);
        if (tenant == null)
        {
            return NotFound(new { success = false, message = "Tenant not found" });
        }

        tenant.Email = request.Email ?? tenant.Email;
        tenant.Phone = request.Phone ?? tenant.Phone;
        // tenant.EmergencyContactName = request.EmergencyContactName ?? tenant.EmergencyContactName; // Property doesn't exist
        // tenant.EmergencyContactPhone = request.EmergencyContactPhone ?? tenant.EmergencyContactPhone; // Property doesn't exist
        tenant.ModifiedDate = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new { success = true, message = "Profile updated successfully" });
    }

    #endregion

    #region Lease APIs

    /// <summary>
    /// Get all active leases for tenant
    /// GET: api/tenant/{tenantId}/leases
    /// </summary>
    [HttpGet("{tenantId}/leases")]
    public async Task<IActionResult> GetLeases(int tenantId)
    {
        if (!IsAuthorizedForTenant(tenantId)) return Forbid();
        var leases = await _context.Leases
            .AsNoTracking()
            .Include(l => l.Unit)
                .ThenInclude(u => u.Property)
            .Where(l => l.TenantId == tenantId && l.Status == "Active")
            .OrderByDescending(l => l.LeaseStartDate)
            .ToListAsync();

        // Latest legal case per lease (read-only status for the tenant banner).
        // Only status/type/next-hearing are exposed — never notes or amount.
        var leaseIds = leases.Select(l => l.LeaseId).ToList();
        var caseByLease = await GetLatestCasesByLeaseAsync(tenantId, leaseIds);

        return Ok(new
        {
            success = true,
            data = leases.Select(l =>
            {
                caseByLease.TryGetValue(l.LeaseId, out var lc);
                return new
                {
                    leaseId = l.LeaseId,
                    propertyName = l.Unit?.Property?.PropertyName,
                    propertyAddress = l.Unit?.Property?.Address,
                    area = l.Unit?.Property?.Area,
                    unitNumber = l.Unit?.UnitNumber,
                    monthlyRent = l.MonthlyRent,
                    leaseStartDate = l.LeaseStartDate,
                    leaseEndDate = l.LeaseEndDate,
                    paymentDueDay = l.PaymentDueDay,
                    securityDeposit = l.SecurityDeposit,
                    status = l.Status,
                    legalCaseStatus = lc?.Status,
                    legalCaseType = lc?.CaseType,
                    legalCaseNextHearing = lc?.NextHearingDate
                };
            })
        });
    }

    /// <summary>
    /// Get lease details by ID
    /// GET: api/tenant/{tenantId}/leases/{leaseId}
    /// </summary>
    [HttpGet("{tenantId}/leases/{leaseId}")]
    public async Task<IActionResult> GetLeaseDetails(int tenantId, int leaseId)
    {
        if (!IsAuthorizedForTenant(tenantId)) return Forbid();
        var lease = await _context.Leases
            .Include(l => l.Unit)
                .ThenInclude(u => u.Property)
            .FirstOrDefaultAsync(l => l.LeaseId == leaseId && l.TenantId == tenantId);

        if (lease == null)
        {
            return NotFound(new { success = false, message = "Lease not found" });
        }

        var caseByLease = await GetLatestCasesByLeaseAsync(tenantId, new List<int> { lease.LeaseId });
        caseByLease.TryGetValue(lease.LeaseId, out var lc);

        return Ok(new
        {
            success = true,
            data = new
            {
                leaseId = lease.LeaseId,
                propertyName = lease.Unit?.Property?.PropertyName,
                propertyAddress = lease.Unit?.Property?.Address,
                area = lease.Unit?.Property?.Area,
                // city = lease.Unit?.Property?.City, // Property doesn't have City property
                unitNumber = lease.Unit?.UnitNumber,
                monthlyRent = lease.MonthlyRent,
                leaseStartDate = lease.LeaseStartDate,
                leaseEndDate = lease.LeaseEndDate,
                paymentDueDay = lease.PaymentDueDay,
                securityDeposit = lease.SecurityDeposit,
                status = lease.Status,
                legalCaseStatus = lc?.Status,
                legalCaseType = lc?.CaseType,
                legalCaseNextHearing = lc?.NextHearingDate
                // terms = lease.Terms // Lease doesn't have Terms property
            }
        });
    }

    /// <summary>
    /// Latest legal case per lease for a tenant, keyed by LeaseId. Query filters
    /// are ignored (the tenant is already authorized for these leases) so a
    /// company-scope filter can't hide the tenant's own case.
    /// </summary>
    private async Task<Dictionary<int, RentCollection.Core.Entities.LegalCase>> GetLatestCasesByLeaseAsync(
        int tenantId, List<int> leaseIds)
    {
        if (leaseIds.Count == 0) return new();
        var cases = await _context.LegalCases
            .IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.TenantId == tenantId && leaseIds.Contains(c.LeaseId))
            .OrderByDescending(c => c.FiledDate).ThenByDescending(c => c.LegalCaseId)
            .ToListAsync();
        return cases.GroupBy(c => c.LeaseId).ToDictionary(g => g.Key, g => g.First());
    }

    #endregion

    #region Payment APIs

    /// <summary>
    /// Get payment history for tenant
    /// GET: api/tenant/{tenantId}/payments
    /// </summary>
    [HttpGet("{tenantId}/payments")]
    public async Task<IActionResult> GetPayments(int tenantId, [FromQuery] int? pageNumber = 1, [FromQuery] int? pageSize = 20)
    {
        if (!IsAuthorizedForTenant(tenantId)) return Forbid();
        var query = _context.Payments
            .Include(p => p.Lease)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .Where(p => p.Lease.TenantId == tenantId)
            .OrderByDescending(p => p.PaymentDate);

        var totalCount = await query.CountAsync();
        var payments = await query
            .Skip((pageNumber.Value - 1) * pageSize.Value)
            .Take(pageSize.Value)
            .ToListAsync();

        return Ok(new
        {
            success = true,
            data = new
            {
                totalCount,
                pageNumber,
                pageSize,
                payments = payments.Select(p => new
                {
                    paymentId = p.PaymentId,
                    propertyName = p.Lease?.Unit?.Property?.PropertyName,
                    unitNumber = p.Lease?.Unit?.UnitNumber,
                    amountPaid = p.AmountPaid,
                    paymentDate = p.PaymentDate,
                    paymentMethod = p.PaymentMethod,
                    transactionReference = p.TransactionReference,
                    paymentStatus = p.PaymentStatus,
                    notes = p.Notes
                })
            }
        });
    }

    /// <summary>
    /// Get payment details by ID
    /// GET: api/tenant/{tenantId}/payments/{paymentId}
    /// </summary>
    [HttpGet("{tenantId}/payments/{paymentId}")]
    public async Task<IActionResult> GetPaymentDetails(int tenantId, int paymentId)
    {
        if (!IsAuthorizedForTenant(tenantId)) return Forbid();
        var payment = await _context.Payments
            .Include(p => p.Lease)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .FirstOrDefaultAsync(p => p.PaymentId == paymentId && p.Lease.TenantId == tenantId);

        if (payment == null)
        {
            return NotFound(new { success = false, message = "Payment not found" });
        }

        return Ok(new
        {
            success = true,
            data = new
            {
                paymentId = payment.PaymentId,
                leaseId = payment.LeaseId,
                propertyName = payment.Lease?.Unit?.Property?.PropertyName,
                unitNumber = payment.Lease?.Unit?.UnitNumber,
                amountPaid = payment.AmountPaid,
                paymentDate = payment.PaymentDate,
                paymentMethod = payment.PaymentMethod,
                transactionReference = payment.TransactionReference,
                paymentStatus = payment.PaymentStatus,
                notes = payment.Notes
            }
        });
    }

    /// <summary>
    /// Get pending invoices for tenant (for Make Payment screen)
    /// GET: api/tenant/{tenantId}/invoices/pending
    /// </summary>
    [HttpGet("{tenantId}/invoices/pending")]
    public async Task<IActionResult> GetPendingInvoices(int tenantId)
    {
        if (!IsAuthorizedForTenant(tenantId)) return Forbid();
        var pendingInvoices = await _context.Invoices
            .Include(i => i.Lease)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .Where(i => i.TenantId == tenantId && (i.Status == "Pending" || i.Status == "Overdue"))
            .OrderByDescending(i => i.DueDate)
            .ToListAsync();

        return Ok(new
        {
            success = true,
            data = pendingInvoices.Select(i => new
            {
                invoiceId = i.InvoiceId,
                leaseId = i.LeaseId,
                tenantId = i.TenantId,
                propertyId = i.Lease?.Unit?.PropertyId,
                propertyName = i.Lease?.Unit?.Property?.PropertyName,
                unitNumber = i.Lease?.Unit?.UnitNumber,
                totalAmount = i.TotalAmount,
                paidAmount = i.PaidAmount,
                balanceAmount = i.TotalAmount - i.PaidAmount,
                dueDate = i.DueDate,
                invoiceDate = i.InvoiceDate,
                status = i.Status,
                description = i.Notes ?? $"{i.InvoiceType} for {i.Lease?.Unit?.Property?.PropertyName}"
            })
        });
    }

    /// <summary>
    /// List all invoices for a tenant. Supports pagination and an optional
    /// status filter ("Pending", "Paid", "Partially Paid", "Overdue", "Cancelled").
    /// Pass status="Outstanding" to get Pending + Overdue together (the most
    /// common UI filter). Without a status, every invoice is returned.
    /// GET: api/tenant/{tenantId}/invoices?status=&pageNumber=&pageSize=
    /// </summary>
    [HttpGet("{tenantId}/invoices")]
    public async Task<IActionResult> GetInvoices(
        int tenantId,
        [FromQuery] string? status = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!IsAuthorizedForTenant(tenantId)) return Forbid();

        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        // Pure read — disable change tracking.
        var query = _context.Invoices
            .AsNoTracking()
            .Include(i => i.Lease)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .Where(i => i.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            // "Outstanding" is a UI convenience — fold Pending + Overdue.
            if (string.Equals(status, "Outstanding", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(i => i.Status == "Pending" || i.Status == "Overdue");
            }
            else
            {
                query = query.Where(i => i.Status == status);
            }
        }

        var totalCount = await query.CountAsync();
        var invoices = await query
            .OrderByDescending(i => i.DueDate)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var today = DateTime.UtcNow.Date;

        return Ok(new
        {
            success = true,
            data = new
            {
                totalCount,
                pageNumber,
                pageSize,
                invoices = invoices.Select(i => new
                {
                    invoiceId = i.InvoiceId,
                    leaseId = i.LeaseId,
                    tenantId = i.TenantId,
                    propertyId = i.Lease?.Unit?.PropertyId,
                    propertyName = i.Lease?.Unit?.Property?.PropertyName,
                    unitNumber = i.Lease?.Unit?.UnitNumber,
                    invoiceType = i.InvoiceType,
                    totalAmount = i.TotalAmount,
                    paidAmount = i.PaidAmount,
                    balanceAmount = i.TotalAmount - i.PaidAmount,
                    invoiceDate = i.InvoiceDate,
                    dueDate = i.DueDate,
                    // Derive Overdue at read time when the persisted status is
                    // still Pending but the due date has passed. Cheap and
                    // avoids relying on a background job to flip the column.
                    status = (i.Status == "Pending" && i.DueDate.Date < today)
                        ? "Overdue"
                        : i.Status,
                    description = i.Notes ?? $"{i.InvoiceType} for {i.Lease?.Unit?.Property?.PropertyName}"
                })
            }
        });
    }

    /// <summary>
    /// Get full details for a single invoice, including related payments.
    /// GET: api/tenant/{tenantId}/invoices/{invoiceId}
    /// </summary>
    [HttpGet("{tenantId}/invoices/{invoiceId}")]
    public async Task<IActionResult> GetInvoiceDetails(int tenantId, int invoiceId)
    {
        if (!IsAuthorizedForTenant(tenantId)) return Forbid();

        var invoice = await _context.Invoices
            .AsNoTracking()
            .Include(i => i.Lease)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .FirstOrDefaultAsync(i => i.InvoiceId == invoiceId && i.TenantId == tenantId);

        if (invoice == null)
            return NotFound(new { success = false, message = "Invoice not found" });

        var payments = await _context.Payments
            .AsNoTracking()
            .Where(p => p.InvoiceId == invoiceId)
            .OrderByDescending(p => p.PaymentDate)
            .Select(p => new
            {
                paymentId = p.PaymentId,
                paymentDate = p.PaymentDate,
                amountPaid = p.AmountPaid,
                paymentType = p.PaymentType.ToString(),
                status = p.Status.ToString(),
                // A receipt only exists once money has actually been
                // confirmed. Cash requests get a receipt number at request
                // time (for the office), but the tenant must not see one for
                // a payment that is still pending/rejected.
                receiptNumber = p.Status == RentCollection.Core.Enums.PaymentStatus.Completed ? p.ReceiptNumber : null,
                transactionReference = p.TransactionReference
            })
            .ToListAsync();

        var today = DateTime.UtcNow.Date;
        var derivedStatus = (invoice.Status == "Pending" && invoice.DueDate.Date < today)
            ? "Overdue"
            : invoice.Status;

        return Ok(new
        {
            success = true,
            data = new
            {
                invoiceId = invoice.InvoiceId,
                leaseId = invoice.LeaseId,
                tenantId = invoice.TenantId,
                propertyId = invoice.Lease?.Unit?.PropertyId,
                propertyName = invoice.Lease?.Unit?.Property?.PropertyName,
                propertyAddress = invoice.Lease?.Unit?.Property?.Address,
                unitNumber = invoice.Lease?.Unit?.UnitNumber,
                invoiceType = invoice.InvoiceType,
                totalAmount = invoice.TotalAmount,
                paidAmount = invoice.PaidAmount,
                balanceAmount = invoice.TotalAmount - invoice.PaidAmount,
                invoiceDate = invoice.InvoiceDate,
                dueDate = invoice.DueDate,
                status = derivedStatus,
                notes = invoice.Notes,
                payments
            }
        });
    }

    /// <summary>
    /// Submit a payment from the tenant app. KNET and Bank Transfer have their
    /// own dedicated endpoints with idempotency, amount validation, audit, etc.
    /// This generic endpoint was previously a security/financial bypass that
    /// wrote `Payment` rows directly without locking the invoice, without
    /// touching `Invoice.PaidAmount`, and without an idempotency key.
    ///
    /// It is now disabled. Clients must use:
    ///   - POST /api/payments/knet/initiate     for KNET
    ///   - POST /api/payments/bank/upload       for bank transfer
    ///   - In-person cash collection            via the admin
    /// </summary>
    [HttpPost("{tenantId}/payments")]
    public IActionResult SubmitPayment(int tenantId, [FromBody] SubmitPaymentRequest request)
    {
        if (!IsAuthorizedForTenant(tenantId)) return Forbid();
        return BadRequest(new
        {
            success = false,
            message = "Direct payment submission is disabled. Use /api/payments/knet/initiate or /api/payments/bank/upload."
        });
    }

    #endregion

    #region Device / Push Token Registration (Sprint 26)

    /// <summary>
    /// Register an FCM device token for push notifications. Called by the
    /// Flutter app after login (and on token rotation). Idempotent: if the
    /// (Tenant, Token) pair already exists we just bump LastSeenAt; if the
    /// DeviceId matches but Token rotated we replace the token.
    /// POST: api/tenant/{tenantId}/devices
    /// </summary>
    [HttpPost("{tenantId}/devices")]
    public async Task<IActionResult> RegisterDevice(int tenantId, [FromBody] DeviceRegisterRequest req)
    {
        if (!IsAuthorizedForTenant(tenantId)) return Forbid();

        if (string.IsNullOrWhiteSpace(req?.Token))
        {
            return BadRequest(new { success = false, message = "Token is required" });
        }

        var now = DateTime.UtcNow;
        var token = req.Token!.Trim();
        var platform = (req.Platform ?? "unknown").ToLowerInvariant();

        // Idempotency strategy:
        // 1. If THIS exact token is already on file for THIS tenant — refresh LastSeenAt.
        // 2. Else if the device (by DeviceId) is on file but the token rotated — replace token.
        // 3. Else insert a new row.
        var existingByToken = await _context.TenantDeviceTokens
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Token == token);
        if (existingByToken != null)
        {
            existingByToken.IsActive = true;
            existingByToken.LastSeenAt = now;
            existingByToken.DeactivatedAt = null;
            existingByToken.DeactivationReason = null;
        }
        else
        {
            RentCollection.Core.Entities.TenantDeviceToken? existingByDevice = null;
            if (!string.IsNullOrEmpty(req.DeviceId))
            {
                existingByDevice = await _context.TenantDeviceTokens
                    .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.DeviceId == req.DeviceId);
            }
            if (existingByDevice != null)
            {
                existingByDevice.Token = token;
                existingByDevice.Platform = platform;
                existingByDevice.IsActive = true;
                existingByDevice.LastSeenAt = now;
                existingByDevice.DeactivatedAt = null;
                existingByDevice.DeactivationReason = null;
            }
            else
            {
                _context.TenantDeviceTokens.Add(new RentCollection.Core.Entities.TenantDeviceToken
                {
                    TenantId = tenantId,
                    Token = token,
                    Platform = platform,
                    DeviceId = req.DeviceId,
                    Locale = req.Locale,
                    RegisteredAt = now,
                    LastSeenAt = now,
                    IsActive = true,
                });
            }
        }

        await _context.SaveChangesAsync();
        return Ok(new { success = true });
    }

    #endregion

    #region Maintenance Request APIs

    /// <summary>
    /// Get all maintenance requests for tenant
    /// GET: api/tenant/{tenantId}/maintenance-requests
    /// </summary>
    [HttpGet("{tenantId}/maintenance-requests")]
    public async Task<IActionResult> GetMaintenanceRequests(int tenantId, [FromQuery] string? status = null)
    {
        if (!IsAuthorizedForTenant(tenantId)) return Forbid();
        var query = _context.MaintenanceRequests
            .Include(m => m.Property)
            .Include(m => m.Unit)
            .Where(m => m.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(m => m.Status == status);
        }

        var requests = await query
            .OrderByDescending(m => m.RequestDate)
            .ToListAsync();

        return Ok(new
        {
            success = true,
            data = requests.Select(m => new
            {
                requestId = m.RequestId,
                propertyName = m.Property?.PropertyName,
                unitNumber = m.Unit?.UnitNumber,
                category = m.Category,
                priority = m.Priority,
                description = m.Description,
                status = m.Status,
                requestDate = m.RequestDate,
                completedDate = m.CompletedDate,
                notes = m.Notes
            })
        });
    }

    /// <summary>
    /// Get maintenance request details by ID
    /// GET: api/tenant/{tenantId}/maintenance-requests/{requestId}
    /// </summary>
    [HttpGet("{tenantId}/maintenance-requests/{requestId}")]
    public async Task<IActionResult> GetMaintenanceRequestDetails(int tenantId, int requestId)
    {
        if (!IsAuthorizedForTenant(tenantId)) return Forbid();
        var request = await _context.MaintenanceRequests
            .Include(m => m.Property)
            .Include(m => m.Unit)
            .Include(m => m.AssignedToUser)
            .FirstOrDefaultAsync(m => m.RequestId == requestId && m.TenantId == tenantId);

        if (request == null)
        {
            return NotFound(new { success = false, message = "Maintenance request not found" });
        }

        return Ok(new
        {
            success = true,
            data = new
            {
                requestId = request.RequestId,
                propertyName = request.Property?.PropertyName,
                propertyAddress = request.Property?.Address,
                unitNumber = request.Unit?.UnitNumber,
                category = request.Category,
                priority = request.Priority,
                description = request.Description,
                status = request.Status,
                requestDate = request.RequestDate,
                completedDate = request.CompletedDate,
                estimatedCost = request.EstimatedCost,
                actualCost = request.ActualCost,
                assignedTo = request.AssignedToUser != null ? $"{request.AssignedToUser.FirstName} {request.AssignedToUser.LastName}" : null,
                notes = request.Notes
            }
        });
    }

    /// <summary>
    /// Create a new maintenance request
    /// POST: api/tenant/{tenantId}/maintenance-requests
    /// </summary>
    [HttpPost("{tenantId}/maintenance-requests")]
    public async Task<IActionResult> CreateMaintenanceRequest(int tenantId, [FromBody] CreateMaintenanceRequest request)
    {
        if (!IsAuthorizedForTenant(tenantId)) return Forbid();
        var lease = await _context.Leases
            .Include(l => l.Unit)
            .FirstOrDefaultAsync(l => l.LeaseId == request.LeaseId && l.TenantId == tenantId);

        if (lease == null)
        {
            return NotFound(new { success = false, message = "Lease not found" });
        }

        var now = DateTime.UtcNow;
        var maintenanceRequest = new MaintenanceRequest
        {
            PropertyId = lease.Unit.PropertyId,
            UnitId = lease.UnitId,
            TenantId = tenantId,
            Category = request.Category,
            Priority = request.Priority,
            Description = request.Description,
            Status = "Open",
            RequestDate = now,
            DueDate = now.AddHours(MaintenanceRequest.SlaHoursForPriority(request.Priority)),
            CreatedDate = now
        };

        _context.MaintenanceRequests.Add(maintenanceRequest);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = "Maintenance request created successfully",
            data = new { requestId = maintenanceRequest.RequestId }
        });
    }

    /// <summary>
    /// Upload one or more photos to a maintenance request. Multipart form
    /// upload, field name "photos" (repeatable). Optional form field
    /// "kind" = Before|After|Other (default Before for tenant uploads).
    /// </summary>
    [HttpPost("{tenantId}/maintenance-requests/{requestId}/photos")]
    [RequestSizeLimit(20 * 1024 * 1024)]  // 20 MB across all files
    public async Task<IActionResult> UploadMaintenancePhotos(
        int tenantId,
        int requestId,
        [FromForm] List<IFormFile> photos,
        [FromForm] string? kind,
        [FromServices] IDocumentStore store)
    {
        if (!IsAuthorizedForTenant(tenantId)) return Forbid();

        var req = await _context.MaintenanceRequests
            .FirstOrDefaultAsync(m => m.RequestId == requestId && m.TenantId == tenantId);
        if (req == null) return NotFound(new { success = false, message = "Maintenance request not found" });

        if (photos == null || photos.Count == 0)
            return BadRequest(new { success = false, message = "No photos uploaded" });

        var allowed = new[] { "image/jpeg", "image/png", "image/heic", "image/webp" };
        var saved = new List<object>();
        var resolvedKind = kind switch
        {
            "After" => MaintenancePhotoKind.After,
            "Other" => MaintenancePhotoKind.Other,
            _ => MaintenancePhotoKind.Before
        };

        foreach (var file in photos)
        {
            if (file.Length == 0 || file.Length > 5 * 1024 * 1024)
                continue;  // skip empty / >5MB individual files
            if (!allowed.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
                continue;
            // SECURITY (audit H11): the content-type above is client-supplied;
            // confirm the real bytes are an allowed image before storing.
            if (!RentCollection.Application.Services.FileSignatureValidator.IsAllowed(file))
                continue;

            using var stream = file.OpenReadStream();
            var stored = await store.SaveAsync(stream, $"maintenance/{requestId}", file.FileName, file.ContentType);

            var photo = new MaintenancePhoto
            {
                RequestId = requestId,
                FilePath = stored.Key,
                FileName = Path.GetFileName(file.FileName),
                FileSize = stored.SizeBytes,
                ContentType = stored.ContentType,
                Kind = resolvedKind,
                UploadedBy = $"tenant:{tenantId}",
            };
            _context.MaintenancePhotos.Add(photo);
            saved.Add(new { fileName = photo.FileName, key = photo.FilePath, photo.Kind });
        }

        if (saved.Count == 0)
            return BadRequest(new { success = false, message = "No valid images. Allowed: JPEG/PNG/HEIC/WEBP, max 5 MB each." });

        await _context.SaveChangesAsync();
        return Ok(new { success = true, data = saved });
    }

    /// <summary>
    /// List photos attached to a maintenance request. Returns presigned URLs
    /// (or local relative paths) so the client can render the images directly.
    /// </summary>
    [HttpGet("{tenantId}/maintenance-requests/{requestId}/photos")]
    public async Task<IActionResult> GetMaintenancePhotos(
        int tenantId,
        int requestId,
        [FromServices] IDocumentStore store)
    {
        if (!IsAuthorizedForTenant(tenantId)) return Forbid();

        // SECURITY (audit H5): self-auth on tenantId is not enough — the requestId
        // must also belong to this tenant, or tenant A could pass another tenant's
        // requestId and read their maintenance photos. Verify ownership first.
        var ownsRequest = await _context.MaintenanceRequests
            .AsNoTracking()
            .AnyAsync(m => m.RequestId == requestId && m.TenantId == tenantId);
        if (!ownsRequest) return NotFound(new { success = false, message = "Request not found" });

        var photos = await _context.MaintenancePhotos
            .AsNoTracking()
            .Where(p => p.RequestId == requestId)
            .OrderByDescending(p => p.UploadedDate)
            .ToListAsync();

        var data = new List<object>();
        foreach (var p in photos)
        {
            var url = await store.GetReadUrlAsync(p.FilePath, TimeSpan.FromHours(1));
            data.Add(new
            {
                p.MaintenancePhotoId,
                p.FileName,
                p.ContentType,
                p.Kind,
                p.FileSize,
                uploadedAt = p.UploadedDate,
                url
            });
        }
        return Ok(new { success = true, data });
    }

    #endregion

    #region Dashboard APIs

    /// <summary>
    /// Get dashboard summary data for tenant
    /// GET: api/tenant/{tenantId}/dashboard
    /// </summary>
    [HttpGet("{tenantId}/dashboard")]
    public async Task<IActionResult> GetDashboard(int tenantId)
    {
        if (!IsAuthorizedForTenant(tenantId)) return Forbid();
        // Dashboard is a pure read — disable EF tracking to skip change-detection overhead.
        var tenant = await _context.Tenants
            .AsNoTracking()
            .Include(t => t.Company)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId);

        if (tenant == null)
        {
            return NotFound(new { success = false, message = "Tenant not found" });
        }

        var activeLeases = await _context.Leases
            .AsNoTracking()
            .Include(l => l.Unit)
                .ThenInclude(u => u.Property)
            .Where(l => l.TenantId == tenantId && l.Status == "Active")
            .ToListAsync();

        // Latest legal case per active lease so the dashboard can surface the
        // same read-only banner the leases screen shows (status/type/hearing only).
        var caseByLease = await GetLatestCasesByLeaseAsync(
            tenantId, activeLeases.Select(l => l.LeaseId).ToList());

        var recentPayments = await _context.Payments
            .AsNoTracking()
            .Include(p => p.Lease)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .Where(p => p.Lease.TenantId == tenantId)
            .OrderByDescending(p => p.PaymentDate)
            .Take(5)
            .ToListAsync();

        var pendingMaintenanceRequests = await _context.MaintenanceRequests
            .AsNoTracking()
            .Include(m => m.Property)
            .Include(m => m.Unit)
            .Where(m => m.TenantId == tenantId && m.Status != "Completed" && m.Status != "Cancelled")
            .OrderByDescending(m => m.RequestDate)
            .ToListAsync();

        return Ok(new
        {
            success = true,
            data = new
            {
                tenant = new
                {
                    firstName = tenant.FirstName,
                    lastName = tenant.LastName,
                    email = tenant.Email,
                    phone = tenant.Phone
                },
                statistics = new
                {
                    activeLeasesCount = activeLeases.Count,
                    totalPaymentsCount = await _context.Payments.CountAsync(p => p.Lease.TenantId == tenantId),
                    pendingMaintenanceRequestsCount = pendingMaintenanceRequests.Count
                },
                activeLeases = activeLeases.Select(l =>
                {
                    caseByLease.TryGetValue(l.LeaseId, out var lc);
                    return new
                    {
                        leaseId = l.LeaseId,
                        propertyName = l.Unit?.Property?.PropertyName,
                        unitNumber = l.Unit?.UnitNumber,
                        monthlyRent = l.MonthlyRent,
                        paymentDueDay = l.PaymentDueDay,
                        legalCaseStatus = lc?.Status,
                        legalCaseType = lc?.CaseType,
                        legalCaseNextHearing = lc?.NextHearingDate
                    };
                }),
                recentPayments = recentPayments.Select(p => new
                {
                    paymentId = p.PaymentId,
                    propertyName = p.Lease?.Unit?.Property?.PropertyName,
                    amountPaid = p.AmountPaid,
                    paymentDate = p.PaymentDate,
                    paymentStatus = p.PaymentStatus
                }),
                maintenanceRequests = pendingMaintenanceRequests.Select(m => new
                {
                    requestId = m.RequestId,
                    propertyName = m.Property?.PropertyName,
                    category = m.Category,
                    priority = m.Priority,
                    status = m.Status,
                    requestDate = m.RequestDate
                })
            }
        });
    }

    #endregion

    #region Notification APIs

    /// <summary>
    /// Get notifications for tenant (upcoming payments, maintenance updates, etc.)
    /// GET: api/tenant/{tenantId}/notifications
    /// </summary>
    [HttpGet("{tenantId}/notifications")]
    public async Task<IActionResult> GetNotifications(int tenantId)
    {
        if (!IsAuthorizedForTenant(tenantId)) return Forbid();
        var notifications = new List<object>();

        // Check for upcoming rent payments (next 7 days)
        var leases = await _context.Leases
            .Include(l => l.Unit)
                .ThenInclude(u => u.Property)
            .Where(l => l.TenantId == tenantId && l.Status == "Active")
            .ToListAsync();

        var today = DateTime.UtcNow.Date;
        foreach (var lease in leases)
        {
            var nextPaymentDay = new DateTime(today.Year, today.Month, lease.PaymentDueDay);
            if (nextPaymentDay < today)
            {
                nextPaymentDay = nextPaymentDay.AddMonths(1);
            }

            var daysUntilPayment = (nextPaymentDay - today).Days;
            if (daysUntilPayment <= 7)
            {
                notifications.Add(new
                {
                    type = "payment_reminder",
                    title = "Rent Payment Due",
                    message = $"Rent payment of {lease.MonthlyRent:N3} KWD for {lease.Unit?.Property?.PropertyName} is due in {daysUntilPayment} days",
                    date = nextPaymentDay,
                    priority = daysUntilPayment <= 3 ? "high" : "medium"
                });
            }
        }

        // Check for recent maintenance request updates
        var recentUpdates = await _context.MaintenanceRequests
            .Include(m => m.Property)
            .Where(m => m.TenantId == tenantId && m.ModifiedDate.HasValue && m.ModifiedDate.Value > DateTime.UtcNow.AddDays(-7))
            .OrderByDescending(m => m.ModifiedDate)
            .Take(5)
            .ToListAsync();

        foreach (var update in recentUpdates)
        {
            notifications.Add(new
            {
                type = "maintenance_update",
                title = "Maintenance Request Update",
                message = $"Your {update.Category} request for {update.Property?.PropertyName} status: {update.Status}",
                date = update.ModifiedDate,
                priority = "low"
            });
        }

        return Ok(new
        {
            success = true,
            data = notifications.OrderByDescending(n => ((dynamic)n).date)
        });
    }

    #endregion
}

#region Request Models

public class TenantLoginRequest
{
    /// <summary>Tenant's Kuwait Civil ID (matches Tenant.GovernmentId).</summary>
    public string? CivilId { get; set; }

    /// <summary>
    /// Deprecated alias for [CivilId]. Older app builds send this field;
    /// the server treats it as a Civil ID and matches against GovernmentId.
    /// PACI on the Tenant record is a property identifier, not a login key.
    /// </summary>
    [Obsolete("Use CivilId. PACI is a property identifier, not a login key.")]
    public string? PACINumber { get; set; }

    [Required]
    public string Password { get; set; } = string.Empty;

    /// <summary>When true ("Remember me for 30 days"), the issued JWT is
    /// long-lived (RefreshTokenExpirationDays) so the app stays signed in
    /// across cold starts; otherwise it's the short AccessTokenExpirationMinutes.</summary>
    public bool RememberMe { get; set; } = false;
}

public class TenantApiChangePasswordRequest
{
    [Required]
    public int TenantId { get; set; }

    [Required]
    public string OldPassword { get; set; } = string.Empty;

    [Required]
    [MinLength(6)]
    public string NewPassword { get; set; } = string.Empty;
}

public class UpdateProfileRequest
{
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
}

public class SubmitPaymentRequest
{
    [Required]
    public int LeaseId { get; set; }

    [Required]
    [Range(0.001, double.MaxValue)]
    public decimal Amount { get; set; }

    [Required]
    public string PaymentMethod { get; set; } = string.Empty;

    public string? TransactionReference { get; set; }
    public string? Notes { get; set; }
}

// Sprint 26 — payload for POST /api/tenant/{id}/devices
public class DeviceRegisterRequest
{
    [Required]
    public string? Token { get; set; }
    /// <summary>"android" / "ios" / "web".</summary>
    public string? Platform { get; set; }
    /// <summary>Stable per-install ID so token rotations dedupe to one row.</summary>
    public string? DeviceId { get; set; }
    /// <summary>BCP-47 locale (e.g. "en-KW", "ar-KW"). Used for future translation.</summary>
    public string? Locale { get; set; }
}

public class CreateMaintenanceRequest
{
    [Required]
    public int LeaseId { get; set; }

    [Required]
    public string Category { get; set; } = string.Empty;

    [Required]
    public string Priority { get; set; } = string.Empty;

    [Required]
    public string Description { get; set; } = string.Empty;
}

#endregion
