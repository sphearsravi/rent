using Microsoft.AspNetCore.Mvc;
using RentCollection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RentCollection.Web.Controllers.Api;

[ApiController]
[Route("api/diagnostics")]
public class DiagnosticsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DiagnosticsController> _logger;

    public DiagnosticsController(
        ApplicationDbContext context,
        ILogger<DiagnosticsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Test endpoint to check if API is working
    /// GET: api/diagnostics/test
    /// </summary>
    [HttpGet("test")]
    public IActionResult Test()
    {
        return Ok(new
        {
            success = true,
            message = "API is working!",
            timestamp = DateTime.UtcNow,
            environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        });
    }

    /// <summary>
    /// Check current session
    /// GET: api/diagnostics/session
    /// </summary>
    [HttpGet("session")]
    public IActionResult CheckSession()
    {
        var tenantId = HttpContext.Session.GetInt32("TenantId");
        var tenantName = HttpContext.Session.GetString("TenantName");

        return Ok(new
        {
            success = true,
            isLoggedIn = tenantId.HasValue,
            tenantId = tenantId,
            tenantName = tenantName
        });
    }

    /// <summary>
    /// Check invoices for current tenant
    /// GET: api/diagnostics/invoices
    /// </summary>
    [HttpGet("invoices")]
    public async Task<IActionResult> CheckInvoices()
    {
        var tenantId = HttpContext.Session.GetInt32("TenantId");
        if (!tenantId.HasValue)
        {
            return Ok(new
            {
                success = false,
                message = "Not logged in",
                tenantId = (int?)null,
                invoiceCount = 0
            });
        }

        var invoices = await _context.Invoices
            .Where(i => i.TenantId == tenantId.Value && (i.Status == "Pending" || i.Status == "Overdue"))
            .Select(i => new
            {
                i.InvoiceId,
                i.TotalAmount,
                i.DueDate,
                i.Status
            })
            .ToListAsync();

        return Ok(new
        {
            success = true,
            tenantId = tenantId.Value,
            invoiceCount = invoices.Count,
            invoices = invoices
        });
    }

    /// <summary>
    /// Check if uploads directory exists and is writable
    /// GET: api/diagnostics/uploads
    /// </summary>
    [HttpGet("uploads")]
    public IActionResult CheckUploadsDirectory()
    {
        var webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var uploadsPath = Path.Combine(webRootPath, "uploads");

        var exists = Directory.Exists(uploadsPath);
        var canWrite = false;

        if (!exists)
        {
            try
            {
                Directory.CreateDirectory(uploadsPath);
                exists = true;
                canWrite = true;
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    success = false,
                    message = "Cannot create uploads directory",
                    error = ex.Message,
                    path = uploadsPath
                });
            }
        }
        else
        {
            try
            {
                var testFile = Path.Combine(uploadsPath, "test.txt");
                System.IO.File.WriteAllText(testFile, "test");
                System.IO.File.Delete(testFile);
                canWrite = true;
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    success = false,
                    message = "Directory exists but is not writable",
                    error = ex.Message,
                    path = uploadsPath
                });
            }
        }

        return Ok(new
        {
            success = true,
            exists = exists,
            canWrite = canWrite,
            path = uploadsPath
        });
    }
}
