using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Application.Interfaces;
using RentCollection.Infrastructure.Data;
using RentCollection.Web.DTOs.Api;

namespace RentCollection.Web.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ILogger<AuthController> _logger;
    private readonly RentCollection.Web.Services.ILoginThrottle _loginThrottle;

    public AuthController(
        ApplicationDbContext context,
        IJwtTokenService jwtTokenService,
        ILogger<AuthController> logger,
        RentCollection.Web.Services.ILoginThrottle loginThrottle)
    {
        _context = context;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
        _loginThrottle = loginThrottle;
    }

    [HttpPost("login")]
    public async Task<ActionResult<CustomResponseDto<LoginResponseData>>> Login([FromBody] LoginRequest request)
    {
        try
        {
            if (request?.Input == null)
            {
                return Ok(CustomResponseDto<LoginResponseData>.ErrorResponse("Invalid request", 400));
            }

            var username = request.Input.Username;
            var password = request.Input.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                return Ok(CustomResponseDto<LoginResponseData>.ErrorResponse("Username and password are required", 400));
            }

            // SECURITY (audit H2): per-account brute-force lockout.
            if (_loginThrottle.IsLockedOut(username, out _))
            {
                return Ok(CustomResponseDto<LoginResponseData>.ErrorResponse("Too many failed attempts. Please try again later.", 429));
            }

            // SECURITY (audit H3): do NOT allow login by PACINumber. PACI is a
            // shared BUILDING identifier (many tenants in one building share it),
            // not a per-person secret, so it's an unsafe login identity. Look up by
            // the per-tenant email only. (The primary tenant login lives at
            // /api/tenant/login and uses the Civil ID / GovernmentId.)
            var tenant = await _context.Tenants
                .Include(t => t.Company)
                .FirstOrDefaultAsync(t => t.Email == username);

            if (tenant == null)
            {
                _loginThrottle.RecordFailure(username);
                return Ok(CustomResponseDto<LoginResponseData>.ErrorResponse("Invalid credentials", 401));
            }

            // Verify password using BCrypt (matching web portal login behavior)
            if (string.IsNullOrEmpty(tenant.Password) || !BCrypt.Net.BCrypt.Verify(password, tenant.Password))
            {
                _loginThrottle.RecordFailure(username);
                return Ok(CustomResponseDto<LoginResponseData>.ErrorResponse("Invalid credentials", 401));
            }

            _loginThrottle.Reset(username);

            // Check if tenant is active
            if (tenant.Status != "Active")
            {
                return Ok(CustomResponseDto<LoginResponseData>.ErrorResponse("Account is not active", 403));
            }

            // Generate JWT token
            var token = _jwtTokenService.GenerateToken(tenant.TenantId, tenant.Email ?? "",
                RentCollection.Web.Services.JwtTokenService.ComputeSecurityStamp(tenant.Password));

            // Prepare response data
            var responseData = new LoginResponseData
            {
                TenantId = tenant.TenantId,
                FirstName = tenant.FirstName,
                LastName = tenant.LastName,
                FullName = $"{tenant.FirstName} {tenant.LastName}",
                Email = tenant.Email,
                Phone = tenant.Phone,
                Mobile = tenant.Phone, // Using Phone as Mobile since Tenant doesn't have separate Mobile field
                PACINumber = tenant.PACINumber,
                CompanyId = tenant.CompanyId,
                Token = token,
                CreatedDate = tenant.CreatedDate
            };

            return Ok(CustomResponseDto<LoginResponseData>.SuccessResponse(responseData, "Login successful"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login");
            return Ok(CustomResponseDto<LoginResponseData>.ErrorResponse("An error occurred during login", 500));
        }
    }
}
