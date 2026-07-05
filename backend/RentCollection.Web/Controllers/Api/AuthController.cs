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

    public AuthController(
        ApplicationDbContext context,
        IJwtTokenService jwtTokenService,
        ILogger<AuthController> logger)
    {
        _context = context;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
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

            // Find tenant by email or PACI number (lookup without password comparison)
            var tenant = await _context.Tenants
                .Include(t => t.Company)
                .FirstOrDefaultAsync(t =>
                    t.Email == username || t.PACINumber == username);

            if (tenant == null)
            {
                return Ok(CustomResponseDto<LoginResponseData>.ErrorResponse("Invalid credentials", 401));
            }

            // Verify password using BCrypt (matching web portal login behavior)
            if (string.IsNullOrEmpty(tenant.Password) || !BCrypt.Net.BCrypt.Verify(password, tenant.Password))
            {
                return Ok(CustomResponseDto<LoginResponseData>.ErrorResponse("Invalid credentials", 401));
            }

            // Check if tenant is active
            if (tenant.Status != "Active")
            {
                return Ok(CustomResponseDto<LoginResponseData>.ErrorResponse("Account is not active", 403));
            }

            // Generate JWT token
            var token = _jwtTokenService.GenerateToken(tenant.TenantId, tenant.Email ?? "");

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
