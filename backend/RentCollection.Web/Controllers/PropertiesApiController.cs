using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.Controllers;

[Route("api/properties")]
[ApiController]
public class PropertiesApiController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public PropertiesApiController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetProperties()
    {
        var properties = await _context.Properties
            .Select(p => new
            {
                p.PropertyId,
                p.PropertyName,
                p.PropertyType,
                Location = $"{p.Address}, {p.Governorate}"
            })
            .ToListAsync();

        return Ok(properties);
    }

    [HttpGet("{propertyId}/units")]
    public async Task<IActionResult> GetUnitsByProperty(int propertyId)
    {
        var units = await _context.Units
            .Where(u => u.PropertyId == propertyId)
            .Select(u => new
            {
                u.UnitId,
                u.UnitNumber,
                u.Bedrooms,
                RentAmount = u.MonthlyRent,
                u.Status
            })
            .ToListAsync();

        return Ok(units);
    }
}
