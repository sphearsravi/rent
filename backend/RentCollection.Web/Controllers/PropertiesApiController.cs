using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.Controllers;

// SECURITY: back-office lookup API (cookie scheme). Operational roles only, and
// every result is scoped to the caller's company through the Property query
// filter — never read Properties with IgnoreQueryFilters here.
[Route("api/properties")]
[ApiController]
[Authorize(Roles = Roles.Operational)]
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
        // Units carry no company filter of their own, so gate on the property:
        // the filtered Properties DbSet only yields rows in the caller's company
        // (SuperAdmin bypasses the filter). 404 so we don't confirm the id exists.
        var ownsProperty = await _context.Properties.AnyAsync(p => p.PropertyId == propertyId);
        if (!ownsProperty)
        {
            return NotFound();
        }

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
