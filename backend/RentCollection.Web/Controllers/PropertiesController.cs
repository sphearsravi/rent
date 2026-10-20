using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using RentCollection.Core.Entities;
using RentCollection.Core.Enums;
using RentCollection.Infrastructure.Data;
using RentCollection.Web.Helpers;

namespace RentCollection.Web.Controllers;

[Authorize(Roles = Roles.Operational)]
public class PropertiesController : Controller
{
    /// <summary>Per-property figures for the list cards (2026-10 redesign).</summary>
    public sealed record PropertyListStats(decimal RentRoll, int ActiveLeases, int Occupied, int Vacant, int Total,
        decimal VacancyCost, int UnpricedUnits)
    {
        public double OccupancyPercent => Total == 0 ? 0 : Math.Round(100.0 * Occupied / Total, 1);
    }

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

            // 2026-10 list redesign — the numbers an owner actually compares
            // properties by: rent roll (sum of ACTIVE lease rent, not listed
            // unit rent), occupancy and what the vacant units are costing.
            var ids = properties.Select(p => p.PropertyId).ToList();
            var rentRoll = await _context.Leases.AsNoTracking()
                .Where(l => l.Status == LeaseStatuses.Active && ids.Contains(l.Unit.PropertyId))
                .GroupBy(l => l.Unit.PropertyId)
                .Select(g => new { PropertyId = g.Key, Rent = g.Sum(l => l.MonthlyRent), Count = g.Count() })
                .ToListAsync();
            var rentMap = rentRoll.ToDictionary(x => x.PropertyId, x => x);
            ViewBag.PropertyStats = properties.ToDictionary(p => p.PropertyId, p =>
            {
                var units = p.Units ?? new List<Unit>();
                var occupied = units.Count(u => u.Status == UnitStatuses.Occupied);
                var vacant = units.Count(u => u.Status == UnitStatuses.Vacant);
                rentMap.TryGetValue(p.PropertyId, out var rr);
                return new PropertyListStats(
                    RentRoll: rr?.Rent ?? 0m,
                    ActiveLeases: rr?.Count ?? 0,
                    Occupied: occupied,
                    Vacant: vacant,
                    Total: units.Count,
                    VacancyCost: units.Where(u => u.Status == UnitStatuses.Vacant).Sum(u => u.MonthlyRent),
                    UnpricedUnits: units.Count(u => u.MonthlyRent <= 0));
            });

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
    // SECURITY: Location=Client (not Any) — a shared/proxy cache must never
    // serve one company's figures to another company's user.
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> Snapshot()
    {
        // Units carry no company filter of their own — scope them through the
        // (filtered) Properties set so the counts are the caller's company only.
        var units = ScopedUnits();
        var totalProperties = await _context.Properties.CountAsync();
        var totalUnits = await units.CountAsync();
        var occupiedUnits = await units.CountAsync(u => u.Status == "Occupied");
        var vacantUnits = await units.CountAsync(u => u.Status == "Vacant");
        var occupancy = totalUnits > 0 ? (decimal)occupiedUnits / totalUnits * 100m : 0m;
        var potentialMonthly = await units.SumAsync(u => (decimal?)u.MonthlyRent) ?? 0m;
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
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Client, VaryByQueryKeys = new[] { "ids" })]
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

        // Payments have no company filter — pin them to properties the caller
        // can see (filtered DbSet) so ?ids= can't probe another company.
        var paymentsQuery = _context.Payments
            .Where(p => p.Status == PaymentStatus.Completed && p.PropertyId != 0
                && _context.Properties.Any(pr => pr.PropertyId == p.PropertyId));
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
            // ScopedUnits() so the unit→property map only covers our own company.
            var propertyUnits = await ScopedUnits()
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

        // 2026-10 details redesign — the money facts for the header strip and
        // the tenants table: rent roll, outstanding per lease, collected MTD.
        var leaseIds = property.Units.SelectMany(u => u.Leases).Select(l => l.LeaseId).ToList();
        var outstandingByLease = (await _context.Invoices.AsNoTracking()
                .Where(i => leaseIds.Contains(i.LeaseId) && i.Status != InvoiceStatuses.Paid && i.Status != InvoiceStatuses.Cancelled
                            && i.TotalAmount - i.PaidAmount > 0)
                .GroupBy(i => i.LeaseId)
                .Select(g => new { LeaseId = g.Key, Balance = g.Sum(i => i.TotalAmount - i.PaidAmount), Oldest = g.Min(i => i.DueDate) })
                .ToListAsync())
            .ToDictionary(x => x.LeaseId, x => (x.Balance, x.Oldest));
        var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var completed = _context.Payments.AsNoTracking()
            .Where(p => p.PropertyId == property.PropertyId && p.Status == RentCollection.Core.Enums.PaymentStatus.Completed);
        ViewBag.OutstandingByLease = outstandingByLease;
        ViewBag.Outstanding = outstandingByLease.Values.Sum(v => v.Balance);
        ViewBag.CollectedMtd = await completed.Where(p => p.PaymentDate >= monthStart).SumAsync(p => (decimal?)p.AmountPaid) ?? 0m;
        ViewBag.LastPaymentDate = await completed.MaxAsync(p => (DateTime?)p.PaymentDate);
        ViewBag.RentRoll = property.Units.SelectMany(u => u.Leases).Where(l => l.Status == LeaseStatuses.Active).Sum(l => l.MonthlyRent);

        return View(property);
    }

    public async Task<IActionResult> Create()
    {
        ViewData["CompanyId"] = new SelectList(await _context.Companies.ToListAsync(), "CompanyId", "CompanyName");
        ViewData["ManagerId"] = await ManagerSelectListAsync(null);
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("PropertyName,PACINumber,PropertyType,Address,Governorate,Area,Block,BuildingNumber,Street,NumberOfUnits,PurchaseDate,PropertyValue,ManagerId")] Property property,
        List<IFormFile>? Documents, List<string>? DocumentTypes,
        [FromForm(Name = "__SaveAndAddAnother")] string? saveAndAddAnother = null,
        string? unitPrefix = null,
        [FromForm(Name = "CompanyId")] int? postedCompanyId = null)
    {
        // SECURITY: CompanyId is deliberately NOT in the [Bind] list. Only a
        // SuperAdmin may honour the posted Company dropdown; everyone else is
        // pinned to the company on their claim (falling back to their DB record,
        // since the claim is only stamped at login).
        property.CompanyId = 0;
        if (User.IsInRole(Roles.SuperAdmin) && postedCompanyId is > 0)
            property.CompanyId = postedCompanyId.Value;
        if (property.CompanyId <= 0)
        {
            var scoped = ScopedCompanyId();
            if (scoped is > 0) property.CompanyId = scoped.Value;
        }
        if (property.CompanyId <= 0)
        {
            var uid = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(uid, out var userId))
            {
                var dbCompanyId = await _context.Users
                    .Where(u => u.UserId == userId)
                    .Select(u => u.CompanyId)
                    .FirstOrDefaultAsync();
                if (dbCompanyId is > 0)
                    property.CompanyId = dbCompanyId.Value;
            }
        }
        ModelState.Remove(nameof(Property.CompanyId));
        if (property.CompanyId <= 0)
        {
            ModelState.AddModelError("CompanyId",
                "Your account isn't linked to a company. Ask an admin to set your CompanyId on the Users page.");
        }

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

            // NumberOfUnits is an instruction to create that many units. Auto-
            // create them as default (vacant) units immediately so they show up
            // on the property Details page right away — numbered {prefix}{1..N}.
            // The operator then edits each unit's type / rooms / rent (from the
            // Details list, or the bulk Unit Setup grid). This runs BEFORE the
            // "Save & add another" branch so that path doesn't skip unit creation.
            var unitsCreated = 0;
            if (property.NumberOfUnits > 0)
            {
                var prefix = (unitPrefix ?? string.Empty).Trim();
                var count = Math.Min(property.NumberOfUnits, 500); // sane upper bound
                var now = DateTime.UtcNow;
                for (int i = 1; i <= count; i++)
                {
                    // Sensible default: a 2-bedroom apartment. The operator
                    // edits type / rooms / rent per unit afterwards.
                    _context.Units.Add(new Unit
                    {
                        PropertyId = property.PropertyId,
                        UnitNumber = $"{prefix}{i}",
                        UnitType = UnitTypes.Apartment,
                        Bedrooms = 2,
                        Bathrooms = 1,
                        MonthlyRent = 0m,
                        Status = UnitStatuses.Vacant,
                        SizeBand = UnitSizeBands.Derive(2, UnitTypes.Apartment),
                        CreatedDate = now
                    });
                }
                await _context.SaveChangesAsync();
                unitsCreated = count;
                TempData["SuccessMessage"] =
                    $"Property created with {count} unit(s). Edit each unit to set its type, rooms and rent.";
            }

            // Sprint 23 — "Save & add another" returns to a fresh Create form.
            if (string.Equals(saveAndAddAnother, "true", StringComparison.OrdinalIgnoreCase))
            {
                TempData["PropertyCreated"] = property.PropertyName;
                return RedirectToAction(nameof(Create));
            }

            if (unitsCreated > 0)
                return RedirectToAction(nameof(Details), new { id = property.PropertyId });
            return RedirectToAction(nameof(Index));
        }
        ViewData["CompanyId"] = new SelectList(await _context.Companies.ToListAsync(), "CompanyId", "CompanyName", property.CompanyId);
        ViewData["ManagerId"] = await ManagerSelectListAsync(property.ManagerId);
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
        ViewData["ManagerId"] = await ManagerSelectListAsync(property.ManagerId);
        // How many units actually exist vs. the declared NumberOfUnits — the
        // Edit view uses this to offer an "add the missing units" shortcut into
        // the bulk Unit Setup grid (it never auto-deletes existing units).
        ViewBag.ActualUnitCount = await _context.Units.CountAsync(u => u.PropertyId == property.PropertyId);
        return View(property);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("PropertyId,PropertyName,PACINumber,PropertyType,Address,Governorate,Area,Block,BuildingNumber,Street,NumberOfUnits,PurchaseDate,PropertyValue,ManagerId,AcceptKNET,AcceptCash,AcceptBankTransfer,KNETAccountID,KNETMID,KNETBankName,KNETBankAccountNumber,KnetTranportalId,KnetTranportalPwd,KnetTerminalResourceKey")] Property property)
    {
        _logger.LogInformation("Property Edit POST - ID: {Id}, PropertyId: {PropertyId}, PropertyName: {PropertyName}, ModelState.IsValid: {IsValid}",
            id, property.PropertyId, property.PropertyName, ModelState.IsValid);

        if (id != property.PropertyId)
        {
            _logger.LogWarning("Property Edit ID mismatch: route ID {RouteId} != model PropertyId {PropertyId}", id, property.PropertyId);
            return NotFound();
        }

        // The form still posts CompanyId / Status / CreatedDate (hidden or select);
        // they are intentionally unbound — company/status are never edited here.
        ModelState.Remove(nameof(Property.CompanyId));
        ModelState.Remove(nameof(Property.Status));
        ModelState.Remove(nameof(Property.CreatedDate));

        // LOAD-THEN-COPY. `_context.Update(postedModel)` marked EVERY column
        // modified, so everything the form didn't post (CompanyId, Status,
        // ImageUrl, KNET credentials, …) was overwritten with defaults — i.e.
        // each edit wiped the building's payment gateway config. Loading the
        // tracked row through the filtered DbSet also enforces company scope.
        var existing = await _context.Properties.FirstOrDefaultAsync(p => p.PropertyId == id);
        if (existing == null)
        {
            return NotFound();
        }

        if (ModelState.IsValid)
        {
            try
            {
                // Same NOT NULL guard as Create — clearing an optional address
                // field on edit binds to null and would 500 on UPDATE otherwise.
                NormalizeNotNullStrings(property);

                existing.PropertyName = property.PropertyName;
                existing.PACINumber = property.PACINumber;
                existing.PropertyType = property.PropertyType;
                existing.Address = property.Address;
                existing.Governorate = property.Governorate;
                existing.Area = property.Area;
                existing.Block = property.Block;
                existing.BuildingNumber = property.BuildingNumber;
                existing.Street = property.Street;
                existing.NumberOfUnits = property.NumberOfUnits;
                existing.PurchaseDate = property.PurchaseDate;
                existing.PropertyValue = property.PropertyValue;
                existing.ManagerId = string.IsNullOrWhiteSpace(property.ManagerId) ? null : property.ManagerId;

                // Payment gateway settings (the form shows all of these).
                existing.AcceptKNET = property.AcceptKNET;
                existing.AcceptCash = property.AcceptCash;
                existing.AcceptBankTransfer = property.AcceptBankTransfer;
                existing.KNETAccountID = property.KNETAccountID;
                existing.KNETMID = property.KNETMID;
                existing.KNETBankName = property.KNETBankName;
                existing.KNETBankAccountNumber = property.KNETBankAccountNumber;
                existing.KnetTranportalId = property.KnetTranportalId;
                // Secrets: an empty password box means "keep what's stored",
                // never "wipe the key".
                if (!string.IsNullOrWhiteSpace(property.KnetTranportalPwd))
                    existing.KnetTranportalPwd = property.KnetTranportalPwd;
                if (!string.IsNullOrWhiteSpace(property.KnetTerminalResourceKey))
                    existing.KnetTerminalResourceKey = property.KnetTerminalResourceKey;

                existing.ModifiedDate = DateTime.UtcNow;
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

        // Re-render with the real (unbound) company/status so the form stays honest.
        property.CompanyId = existing.CompanyId;
        property.Status = existing.Status;
        property.CreatedDate = existing.CreatedDate;
        ViewData["CompanyId"] = new SelectList(await _context.Companies.ToListAsync(), "CompanyId", "CompanyName", property.CompanyId);
        ViewData["ManagerId"] = await ManagerSelectListAsync(property.ManagerId);
        ViewBag.ActualUnitCount = await _context.Units.CountAsync(u => u.PropertyId == id);
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
            // Guard: don't hard-delete a property that still has units or leases
            // (would cascade/FK-error unpredictably and orphan tenants/invoices).
            var unitCount = await _context.Units.CountAsync(u => u.PropertyId == id);
            var leaseCount = await _context.Leases.CountAsync(l => l.Unit != null && l.Unit.PropertyId == id);
            var maintenanceCount = await _context.MaintenanceRequests.CountAsync(m => m.PropertyId == id);
            var paymentCount = await _context.Payments.CountAsync(p => p.PropertyId == id);
            if (unitCount > 0 || leaseCount > 0 || maintenanceCount > 0 || paymentCount > 0)
            {
                TempData["ErrorMessage"] =
                    $"Can't delete '{property.PropertyName}': it still has {unitCount} unit(s), {leaseCount} lease(s), " +
                    $"{maintenanceCount} maintenance request(s) and {paymentCount} payment(s). " +
                    "Remove or reassign them first.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _context.Properties.Remove(property);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Property deleted successfully!";
            }
            catch (DbUpdateException ex)
            {
                // Some other table (documents, audit rows, …) still references
                // the property — surface a friendly message instead of a 500.
                _logger.LogError(ex, "FK violation deleting Property {PropertyId}", id);
                TempData["ErrorMessage"] =
                    $"Can't delete '{property.PropertyName}': other records still reference it. Remove them first.";
            }
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// null = SuperAdmin (unscoped); otherwise the caller's CompanyId claim,
    /// or -1 when the claim is missing so nothing matches. Mirrors
    /// PaymentApprovalController.ScopedCompanyId.
    /// </summary>
    private int? ScopedCompanyId() =>
        User.IsInRole(Roles.SuperAdmin) ? null
        : int.TryParse(User.FindFirst("companyId")?.Value, out var cid) ? cid : -1;

    /// <summary>
    /// Units have no company query filter of their own. Restricting through the
    /// filtered Properties DbSet (a correlated EXISTS) yields only the caller's
    /// company's units; SuperAdmin sees all because the Property filter is bypassed.
    /// </summary>
    private IQueryable<Unit> ScopedUnits() =>
        _context.Units.Where(u => _context.Properties.Any(p => p.PropertyId == u.PropertyId));

    /// <summary>
    /// Property.ManagerId is a FK to ApplicationUser.Id (AspNetUsers, string) —
    /// the old dropdown was built from the legacy Users table (int UserId), so
    /// every selected manager either failed the FK or pointed at nothing.
    /// Source the list from the Identity users, scoped to the caller's company.
    /// </summary>
    private async Task<SelectList> ManagerSelectListAsync(string? selectedId)
    {
        var scope = ScopedCompanyId();
        var q = _context.Set<ApplicationUser>().Where(u => u.IsActive);
        if (scope.HasValue) q = q.Where(u => u.CompanyId == scope.Value);
        var users = await q
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Select(u => new { u.Id, DisplayName = u.FirstName + " " + u.LastName + " (" + u.Email + ")" })
            .ToListAsync();
        return new SelectList(users, "Id", "DisplayName", selectedId);
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
                if (file != null && file.Length > 0 && i < documentTypes.Count && !string.IsNullOrEmpty(documentTypes[i])
                    && UploadValidation.IsAllowed(file))
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
