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
public class SupportController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SupportController> _logger;

    public SupportController(
        ApplicationDbContext context,
        ILogger<SupportController> logger)
    {
        _context = context;
        _logger = logger;
    }

    private bool IsAuthorizedForTenant(int tenantId)
    {
        var claim = User.FindFirst("tenantId")?.Value;
        return int.TryParse(claim, out var claimTenantId) && claimTenantId == tenantId;
    }

    [HttpPost("getmaintenancecategories")]
    public ActionResult<CustomResponseDto<List<MaintenanceCategoryData>>> GetMaintenanceCategories([FromBody] GetMaintenanceCategoriesRequest request)
    {
        try
        {
            if (request?.Input == null || request.Input.Tenantid <= 0)
            {
                return Ok(CustomResponseDto<List<MaintenanceCategoryData>>.ErrorResponse("Invalid tenant ID", 400));
            }
            if (!IsAuthorizedForTenant(request.Input.Tenantid))
            {
                return Ok(CustomResponseDto<List<MaintenanceCategoryData>>.ErrorResponse("Forbidden", 403));
            }

            // Return hardcoded maintenance categories
            // In a production system, these would come from a database table
            var categories = new List<MaintenanceCategoryData>
            {
                new MaintenanceCategoryData
                {
                    CategoryId = 1,
                    CategoryName = "Plumbing",
                    CategoryNameArabic = "السباكة",
                    Description = "Water leaks, pipe repairs, drainage issues",
                    IsActive = true
                },
                new MaintenanceCategoryData
                {
                    CategoryId = 2,
                    CategoryName = "Electrical",
                    CategoryNameArabic = "كهربائي",
                    Description = "Power outages, wiring issues, lighting problems",
                    IsActive = true
                },
                new MaintenanceCategoryData
                {
                    CategoryId = 3,
                    CategoryName = "Air Conditioning",
                    CategoryNameArabic = "تكييف الهواء",
                    Description = "AC repair, cooling issues, filter replacement",
                    IsActive = true
                },
                new MaintenanceCategoryData
                {
                    CategoryId = 4,
                    CategoryName = "Appliances",
                    CategoryNameArabic = "الأجهزة",
                    Description = "Refrigerator, stove, dishwasher repairs",
                    IsActive = true
                },
                new MaintenanceCategoryData
                {
                    CategoryId = 5,
                    CategoryName = "Carpentry",
                    CategoryNameArabic = "نجارة",
                    Description = "Door repairs, cabinet fixes, furniture issues",
                    IsActive = true
                },
                new MaintenanceCategoryData
                {
                    CategoryId = 6,
                    CategoryName = "Painting",
                    CategoryNameArabic = "دهان",
                    Description = "Wall painting, touch-ups, interior/exterior work",
                    IsActive = true
                },
                new MaintenanceCategoryData
                {
                    CategoryId = 7,
                    CategoryName = "Pest Control",
                    CategoryNameArabic = "مكافحة الآفات",
                    Description = "Insect control, rodent removal, preventive treatment",
                    IsActive = true
                },
                new MaintenanceCategoryData
                {
                    CategoryId = 8,
                    CategoryName = "General Maintenance",
                    CategoryNameArabic = "صيانة عامة",
                    Description = "Other maintenance requests",
                    IsActive = true
                }
            };

            return Ok(CustomResponseDto<List<MaintenanceCategoryData>>.SuccessResponse(categories, "Maintenance categories retrieved successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving maintenance categories");
            return Ok(CustomResponseDto<List<MaintenanceCategoryData>>.ErrorResponse("An error occurred while retrieving maintenance categories", 500));
        }
    }

    [HttpPost("savesupportticket")]
    public async Task<ActionResult<CustomResponseDto<SupportTicketResponseData>>> SaveSupportTicket([FromBody] SaveSupportTicketRequest request)
    {
        try
        {
            if (request?.Input == null)
            {
                return Ok(CustomResponseDto<SupportTicketResponseData>.ErrorResponse("Invalid request", 400));
            }

            if (request.Input.Tenantid <= 0 || request.Input.Propertyid <= 0)
            {
                return Ok(CustomResponseDto<SupportTicketResponseData>.ErrorResponse("Tenant ID and Property ID are required", 400));
            }
            if (!IsAuthorizedForTenant(request.Input.Tenantid))
            {
                return Ok(CustomResponseDto<SupportTicketResponseData>.ErrorResponse("Forbidden", 403));
            }

            // Verify tenant exists
            var tenant = await _context.Tenants.FindAsync(request.Input.Tenantid);
            if (tenant == null)
            {
                return Ok(CustomResponseDto<SupportTicketResponseData>.ErrorResponse("Tenant not found", 404));
            }

            // Verify property exists
            var property = await _context.Properties.FindAsync(request.Input.Propertyid);
            if (property == null)
            {
                return Ok(CustomResponseDto<SupportTicketResponseData>.ErrorResponse("Property not found", 404));
            }

            // Map category ID to category name
            var categoryMap = new Dictionary<int, string>
            {
                { 1, "Plumbing" },
                { 2, "Electrical" },
                { 3, "Air Conditioning" },
                { 4, "Appliances" },
                { 5, "Carpentry" },
                { 6, "Painting" },
                { 7, "Pest Control" },
                { 8, "General Maintenance" }
            };

            var categoryName = categoryMap.ContainsKey(request.Input.Maintcategoryid)
                ? categoryMap[request.Input.Maintcategoryid]
                : "General Maintenance";

            // Create maintenance request
            var maintenanceRequest = new MaintenanceRequest
            {
                PropertyId = request.Input.Propertyid,
                UnitId = request.Input.Propertyunitid > 0 ? request.Input.Propertyunitid : null,
                TenantId = request.Input.Tenantid,
                Category = categoryName,
                Priority = "Medium",
                Description = request.Input.Notes ?? "Maintenance request submitted via tenant portal",
                Status = "Open",
                RequestDate = DateTime.UtcNow,
                Notes = request.Input.Images != null && request.Input.Images.Any()
                    ? $"Images: {string.Join(", ", request.Input.Images)}"
                    : null,
                CreatedDate = DateTime.UtcNow
            };

            _context.MaintenanceRequests.Add(maintenanceRequest);
            await _context.SaveChangesAsync();

            var responseData = new SupportTicketResponseData
            {
                TicketId = maintenanceRequest.RequestId,
                TicketNumber = $"MT-{maintenanceRequest.RequestId:D6}",
                Status = maintenanceRequest.Status,
                CreatedDate = maintenanceRequest.CreatedDate,
                Message = "Support ticket created successfully"
            };

            return Ok(CustomResponseDto<SupportTicketResponseData>.SuccessResponse(responseData, "Support ticket created successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating support ticket");
            return Ok(CustomResponseDto<SupportTicketResponseData>.ErrorResponse("An error occurred while creating support ticket", 500));
        }
    }
}
