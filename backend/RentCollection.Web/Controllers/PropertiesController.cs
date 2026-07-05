using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using RentCollection.Core.Entities;
using RentCollection.Core.Enums;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.Controllers;

[Authorize(Roles = Roles.Operational)]
public class PropertiesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<PropertiesController> _logger;

    public PropertiesController(ApplicationDbContext context, IWebHostEnvironment environment, ILogger<PropertiesController> logger)
    {
        _context = context;
        _environment = environment;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        try
        {
            var properties = await _context.Properties
                .Include(p => p.Company)
                .Include(p => p.Manager)
                .Include(p => p.Units)
                .OrderByDescending(p => p.CreatedDate)
                .ToListAsync();

            return View(properties);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading properties list");
            TempData["ErrorMessage"] = "An error occurred while loading properties.";
            return View(new List<Property>());
        }
    }

    #region Properties landing — Sprints 1/2/3 endpoints

    /// <summary>
    /// Portfolio KPI strip for the /Properties landing. One round trip
    /// instead of five per-card aggregations. Same pattern as
    /// /Reports/Snapshot — keeps the page responsive at scale.
    /// </summary>
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Snapshot()
    {
        var totalProperties = await _context.Properties.CountAsync();
        var totalUnits = await _context.Units.CountAsync();
        var occupiedUnits = await _context.Units.CountAsync(u => u.Status == "Occupied");
        var vacantUnits = await _context.Units.CountAsync(u => u.Status == "Available");
        var occupancy = totalUnits > 0 ? (decimal)occupiedUnits / totalUnits * 100m : 0m;
        var potentialMonthly = await _context.Units.SumAsync(u => (decimal?)u.MonthlyRent) ?? 0m;
        var activeProperties = await _context.Properties.CountAsync(p => p.Status == "Active");

        return Json(new
        {
            generatedAt = DateTime.UtcNow,
            totalProperties,
            activeProperties,
            totalUnits,
            occupiedUnits,
            vacantUnits,
            occupancy,
            potentialMonthly,
        });
    }

    /// <summary>
    /// Per-property KPI batch — Rent Collected MTD, Outstanding, Last Payment.
    /// Pass the property ids in the page's render; one round trip returns the
    /// numbers for all visible cards. Falls back to all properties when no
    /// `ids` are passed (small portfolios).
    /// </summary>
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Any, VaryByQueryKeys = new[] { "ids" })]
    public async Task<IActionResult> KpiBatch(string? ids)
    {
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);

        var idList = (ids ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .Where(n => n > 0)
            .Distinct()
            .ToList();

        var paymentsQuery = _context.Payments
            .Where(p => p.Status == PaymentStatus.Completed && p.PropertyId != 0);
        if (idList.Count > 0)
            paymentsQuery = paymentsQuery.Where(p => idList.Contains(p.PropertyId));

        var mtd = await paymentsQuery
            .Where(p => p.PaymentDate >= monthStart && p.PaymentDate <= today)
            .GroupBy(p => p.PropertyId)
            .Select(g => new { PropertyId = g.Key, Total = g.Sum(p => p.AmountPaid) })
            .ToListAsync();

        var lastPayment = await paymentsQuery
            .GroupBy(p => p.PropertyId)
            .Select(g => new { PropertyId = g.Key, LastDate = g.Max(p => p.PaymentDate) })
            .ToListAsync();

        var outstandingQuery = _context.Invoices.Where(i => i.Status != "Paid");
        if (idList.Count > 0)
        {
            // Invoice→Lease→Unit→Property hop; do via PropertyId on the linked Unit.
            var propertyUnits = await _context.Units
                .Where(u => idList.Contains(u.PropertyId))
                .Select(u => new { u.UnitId, u.PropertyId })
                .ToListAsync();
            var unitToProp = propertyUnits.ToDictionary(x => x.UnitId, x => x.PropertyId);
            var unitIds = propertyUnits.Select(u => u.UnitId).ToList();
            var invoices = await _context.Invoices
                .Where(i => i.Status != "Paid")
                .Include(i => i.Lease)
                .Where(i => unitIds.Contains(i.Lease.UnitId))
                .Select(i => new { i.Lease.UnitId, Balance = i.TotalAmount - i.PaidAmount })
                .ToListAsync();
            var outstanding = invoices
                .GroupBy(i => unitToProp.GetValueOrDefault(i.UnitId))
                .Where(g => g.Key != 0)
                .Select(g => new { PropertyId = g.Key, Total = g.Sum(x => x.Balance) })
                .ToList();
            return Json(BuildKpiResponse(idList, mtd, lastPayment, outstanding));
        }

        return Json(BuildKpiResponse(idList, mtd, lastPayment,
            Array.Empty<dynamic>()));
    }

    private static object BuildKpiResponse(
        List<int> propertyIds,
        IEnumerable<dynamic> mtd,
        IEnumerable<dynamic> lastPayment,
        IEnumerable<dynamic> outstanding)
    {
        var mtdMap = mtd.ToDictionary(x => (int)x.PropertyId, x => (decimal)x.Total);
        var lastMap = lastPayment.ToDictionary(x => (int)x.PropertyId, x => (DateTime)x.LastDate);
        var osMap = outstanding.ToDictionary(x => (int)x.PropertyId, x => (decimal)x.Total);
        return propertyIds.Select(id => new
        {
            propertyId = id,
            mtdCollected = mtdMap.GetValueOrDefault(id, 0m),
            outstanding = osMap.GetValueOrDefault(id, 0m),
            lastPaymentDate = lastMap.TryGetValue(id, out var d) ? d : (DateTime?)null,
        });
    }

    /// <summary>
    /// Sprint 2 — Active ⇄ Inactive without bouncing through Edit. POST with
    /// antiforgery from the inline toggle on each card.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStatus(int id)
    {
        var prop = await _context.Properties.FirstOrDefaultAsync(p => p.PropertyId == id);
        if (prop == null) return NotFound();
        prop.Status = prop.Status == "Active" ? "Inactive" : "Active";
        prop.ModifiedDate = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Json(new { status = prop.Status });
    }

    /// <summary>
    /// Sprint 1 — Excel export of the filtered list. Filter params mirror the
    /// view's client filters so the user gets exactly what they're looking at.
    /// </summary>
    public async Task<IActionResult> ExportExcel(
        string? search, string? type, string? governorate, string? status)
    {
        var q = _context.Properties
            .Include(p => p.Units)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(p =>
                p.PropertyName.ToLower().Contains(s) ||
                (p.PACINumber ?? "").ToLower().Contains(s) ||
                p.Address.ToLower().Contains(s) ||
                p.Area.ToLower().Contains(s));
        }
        if (!string.IsNullOrEmpty(type))        q = q.Where(p => p.PropertyType == type);
        if (!string.IsNullOrEmpty(governorate)) q = q.Where(p => p.Governorate == governorate);
        if (!string.IsNullOrEmpty(status))      q = q.Where(p => p.Status == status);

        var rows = await q.OrderBy(p => p.PropertyName).ToListAsync();

        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("Properties");
        var headers = new[] { "Name", "PACI", "Type", "Governorate", "Area", "Address", "Units", "Occupied", "Status", "Value (KWD)" };
        for (int c = 0; c < headers.Length; c++)
        {
            ws.Cells[1, c + 1].Value = headers[c];
            ws.Cells[1, c + 1].Style.Font.Bold = true;
            ws.Cells[1, c + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[1, c + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(99, 102, 241));
            ws.Cells[1, c + 1].Style.Font.Color.SetColor(System.Drawing.Color.White);
        }
        for (int i = 0; i < rows.Count; i++)
        {
            var p = rows[i]; var r = i + 2;
            ws.Cells[r, 1].Value = p.PropertyName;
            ws.Cells[r, 2].Value = p.PACINumber;
            ws.Cells[r, 3].Value = p.PropertyType;
            ws.Cells[r, 4].Value = p.Governorate;
            ws.Cells[r, 5].Value = p.Area;
            ws.Cells[r, 6].Value = $"{p.Address} {p.Block} {p.BuildingNumber}".Trim();
            ws.Cells[r, 7].Value = p.Units.Count;
            ws.Cells[r, 8].Value = p.Units.Count(u => u.Status == "Occupied");
            ws.Cells[r, 9].Value = p.Status;
            ws.Cells[r, 10].Value = p.PropertyValue ?? 0;
            ws.Cells[r, 10].Style.Numberformat.Format = "#,##0.000";
        }
        ws.Cells[ws.Dimension.Address].AutoFitColumns();
        var fileName = $"properties-{DateTime.Today:yyyy-MM-dd}.xlsx";
        return File(pkg.GetAsByteArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    #endregion

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var property = await _context.Properties
            .Include(p => p.Company)
            .Include(p => p.Manager)
            .Include(p => p.Units)
                .ThenInclude(u => u.Leases)
                    .ThenInclude(l => l.Tenant)
            .FirstOrDefaultAsync(p => p.PropertyId == id);

        if (property == null)
        {
            return NotFound();
        }

        return View(property);
    }

    public async Task<IActionResult> Create()
    {
        ViewData["CompanyId"] = new SelectList(await _context.Companies.ToListAsync(), "CompanyId", "CompanyName");
        var users = await _context.Users.ToListAsync();
        ViewData["ManagerId"] = new SelectList(users.Select(u => new { u.UserId, DisplayName = $"{u.FirstName} {u.LastName} ({u.Email})" }), "UserId", "DisplayName");
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("PropertyName,PACINumber,PropertyType,Address,Governorate,Area,Block,BuildingNumber,Street,NumberOfUnits,PurchaseDate,PropertyValue,CompanyId,ManagerId")] Property property,
        List<IFormFile>? Documents, List<string>? DocumentTypes,
        [FromForm(Name = "__SaveAndAddAnother")] string? saveAndAddAnother = null)
    {
        if (ModelState.IsValid)
        {
            // Sprint 23 made Address/Area/Block optional (collapsed section).
            // An empty multipart field model-binds to null, but those columns
            // are NOT NULL — without this the INSERT throws a 500 ("Cannot
            // insert NULL into 'Address'"). Store an unfilled optional field
            // as "" rather than NULL. (Reported by user: unable to create property.)
            NormalizeNotNullStrings(property);
            property.CreatedDate = DateTime.UtcNow;
            _context.Add(property);
            await _context.SaveChangesAsync();

            // Handle document uploads
            if (Documents != null && Documents.Any() && DocumentTypes != null)
            {
                await UploadDocuments(property.PropertyId, Documents, DocumentTypes, "Property");
            }

            TempData["SuccessMessage"] = "Property created successfully!";

            // Sprint 23 — "Save & add another" returns to a fresh Create form.
            if (string.Equals(saveAndAddAnother, "true", StringComparison.OrdinalIgnoreCase))
            {
                TempData["PropertyCreated"] = property.PropertyName;
                return RedirectToAction(nameof(Create));
            }
            return RedirectToAction(nameof(Index));
        }
        ViewData["CompanyId"] = new SelectList(await _context.Companies.ToListAsync(), "CompanyId", "CompanyName", property.CompanyId);
        var users = await _context.Users.ToListAsync();
        ViewData["ManagerId"] = new SelectList(users.Select(u => new { u.UserId, DisplayName = $"{u.FirstName} {u.LastName} ({u.Email})" }), "UserId", "DisplayName", property.ManagerId);
        return View(property);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var property = await _context.Properties.FindAsync(id);
        if (property == null)
        {
            return NotFound();
        }
        ViewData["CompanyId"] = new SelectList(await _context.Companies.ToListAsync(), "CompanyId", "CompanyName", property.CompanyId);
        var users = await _context.Users.ToListAsync();
        ViewData["ManagerId"] = new SelectList(users.Select(u => new { u.UserId, DisplayName = $"{u.FirstName} {u.LastName} ({u.Email})" }), "UserId", "DisplayName", property.ManagerId);
        return View(property);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("PropertyId,PropertyName,PACINumber,PropertyType,Address,Governorate,Area,Block,BuildingNumber,Street,NumberOfUnits,PurchaseDate,PropertyValue,Status,CompanyId,ManagerId,CreatedDate")] Property property)
    {
        _logger.LogInformation("Property Edit POST - ID: {Id}, PropertyId: {PropertyId}, PropertyName: {PropertyName}, ModelState.IsValid: {IsValid}",
            id, property.PropertyId, property.PropertyName, ModelState.IsValid);

        if (id != property.PropertyId)
        {
            _logger.LogWarning("Property Edit ID mismatch: route ID {RouteId} != model PropertyId {PropertyId}", id, property.PropertyId);
            return NotFound();
        }

        if (ModelState.IsValid)
        {
            try
            {
                // Same NOT NULL guard as Create — clearing an optional address
                // field on edit binds to null and would 500 on UPDATE otherwise.
                NormalizeNotNullStrings(property);
                property.ModifiedDate = DateTime.UtcNow;
                _context.Update(property);
                var changes = await _context.SaveChangesAsync();
                _logger.LogInformation("Property {PropertyId} updated successfully ({Changes} changes)", property.PropertyId, changes);
                TempData["SuccessMessage"] = "Property updated successfully!";
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogError(ex, "Concurrency error updating Property {PropertyId}", property.PropertyId);
                if (!PropertyExists(property.PropertyId))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating Property {PropertyId}", property.PropertyId);
                throw;
            }
            return RedirectToAction(nameof(Index));
        }

        // Log validation errors
        foreach (var modelStateKey in ModelState.Keys)
        {
            var value = ModelState[modelStateKey];
            if (value != null && value.Errors.Count > 0)
            {
                foreach (var error in value.Errors)
                {
                    _logger.LogWarning("Property Edit validation failed - Field '{Field}': {ErrorMessage}", modelStateKey, error.ErrorMessage);
                }
            }
        }

        ViewData["CompanyId"] = new SelectList(await _context.Companies.ToListAsync(), "CompanyId", "CompanyName", property.CompanyId);
        var users = await _context.Users.ToListAsync();
        ViewData["ManagerId"] = new SelectList(users.Select(u => new { u.UserId, DisplayName = $"{u.FirstName} {u.LastName} ({u.Email})" }), "UserId", "DisplayName", property.ManagerId);
        return View(property);
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var property = await _context.Properties
            .Include(p => p.Company)
            .Include(p => p.Manager)
            .Include(p => p.Units)
            .FirstOrDefaultAsync(m => m.PropertyId == id);

        if (property == null)
        {
            return NotFound();
        }

        return View(property);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var property = await _context.Properties.FindAsync(id);
        if (property != null)
        {
            _context.Properties.Remove(property);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Property deleted successfully!";
        }

        return RedirectToAction(nameof(Index));
    }

    private bool PropertyExists(int id)
    {
        return _context.Properties.Any(e => e.PropertyId == id);
    }

    // The Property entity's non-nullable string columns are NOT NULL in the
    // database, but empty optional form fields model-bind to null. Coalesce
    // them to "" so a save never violates the NOT NULL constraint.
    private static void NormalizeNotNullStrings(Property p)
    {
        p.PropertyName ??= string.Empty;
        p.PropertyType ??= string.Empty;
        p.Governorate ??= string.Empty;
        p.Address ??= string.Empty;
        p.Area ??= string.Empty;
        p.Block ??= string.Empty;
    }

    private async Task UploadDocuments(int entityId, List<IFormFile> documents, List<string> documentTypes, string entityType)
    {
        try
        {
            // Create uploads directory if it doesn't exist
            var uploadsPath = Path.Combine(_environment.WebRootPath, "uploads", "documents");
            if (!Directory.Exists(uploadsPath))
            {
                Directory.CreateDirectory(uploadsPath);
            }

            for (int i = 0; i < documents.Count; i++)
            {
                var file = documents[i];
                if (file != null && file.Length > 0 && i < documentTypes.Count && !string.IsNullOrEmpty(documentTypes[i]))
                {
                    // Generate unique file name
                    var fileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
                    var filePath = Path.Combine(uploadsPath, fileName);

                    // Save file to disk
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    // Create document record
                    var document = new Document
                    {
                        DocumentName = file.FileName,
                        DocumentType = documentTypes[i],
                        FilePath = Path.Combine("uploads", "documents", fileName),
                        FileSize = file.Length,
                        RelatedEntityType = entityType,
                        RelatedEntityId = entityId,
                        Description = $"{documentTypes[i]} for {entityType}",
                        UploadedBy = User.Identity?.Name ?? "system",
                        UploadedDate = DateTime.UtcNow
                    };

                    _context.Documents.Add(document);
                }
            }

            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading documents for {EntityType} {EntityId}", entityType, entityId);
        }
    }
}
