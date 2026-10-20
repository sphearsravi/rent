using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Core.Enums;
using RentCollection.Infrastructure.Data;
using RentCollection.Web.Models.ViewModels;

namespace RentCollection.Web.Controllers;

/// <summary>
/// Unit management — the "physical inventory" lens on a property. Surfaces:
///   • a portfolio-wide vacancy view (/Units?status=Vacant)
///   • per-property drill-down (/Units?propertyId=N)
///   • details + lease history per unit
///
/// Status flips (Maintenance / Reserved) and the inline edit flow go through
/// this controller; the daily Hangfire job <c>ReconcileUnitStatusAsync</c>
/// keeps Vacant/Occupied honest based on active leases.
/// </summary>
[Authorize(Roles = Roles.Operational)]
public class UnitsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IAuditLogService _audit;
    private readonly ILogger<UnitsController> _logger;

    public UnitsController(
        ApplicationDbContext context,
        IAuditLogService audit,
        ILogger<UnitsController> logger)
    {
        _context = context;
        _audit = audit;
        _logger = logger;
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    /// <summary>
    /// Cross-property unit list. Filters are query-string-driven so a link
    /// like <c>/Units?propertyId=12&amp;status=Vacant&amp;sizeBand=2BHK</c>
    /// is sharable and back-button-safe. Without filters, lists every unit
    /// in the caller's company.
    /// </summary>
    public async Task<IActionResult> Index(
        int? propertyId, string? status, string? unitType,
        string? floor, string? sizeBand, string? search)
    {
        var q = ScopedUnits()
            .Include(u => u.Property)
            // Pending (deposit-awaiting) leases are included so the list can show
            // the unit as Reserved instead of Vacant while the lease is pending.
            .Include(u => u.Leases.Where(l => l.Status == LeaseStatuses.Active || l.Status == LeaseStatuses.Pending))
                .ThenInclude(l => l.Tenant)
            .AsQueryable();

        if (propertyId.HasValue) q = q.Where(u => u.PropertyId == propertyId.Value);
        if (!string.IsNullOrEmpty(status)) q = q.Where(u => u.Status == status);
        if (!string.IsNullOrEmpty(unitType)) q = q.Where(u => u.UnitType == unitType);
        if (!string.IsNullOrWhiteSpace(floor)) q = q.Where(u => u.FloorNumber == floor);
        if (!string.IsNullOrEmpty(sizeBand)) q = q.Where(u => u.SizeBand == sizeBand);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(u =>
                u.UnitNumber.ToLower().Contains(s) ||
                u.Property.PropertyName.ToLower().Contains(s));
        }

        var units = await q
            .OrderBy(u => u.Property.PropertyName)
            .ThenBy(u => u.FloorNumber)
            .ThenBy(u => u.UnitNumber)
            .ToListAsync();

        ViewBag.Properties = await _context.Properties
            .OrderBy(p => p.PropertyName)
            .Select(p => new { p.PropertyId, p.PropertyName })
            .ToListAsync();
        ViewBag.Filter = new
        {
            PropertyId = propertyId,
            Status = status,
            UnitType = unitType,
            Floor = floor,
            SizeBand = sizeBand,
            Search = search,
        };
        return View(units);
    }

    /// <summary>
    /// Portfolio KPI strip: total / vacant / occupied / maintenance + avg rent
    /// + average occupancy. Drives the chip badges + the top cards.
    /// Cached 30s like the other Snapshot endpoints.
    /// </summary>
    // SECURITY: Location=Client (not Any) — a shared/proxy cache must never
    // serve one company's figures to another company's user.
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Client, VaryByQueryKeys = new[] { "propertyId" })]
    public async Task<IActionResult> Snapshot(int? propertyId)
    {
        var q = ScopedUnits();
        if (propertyId.HasValue) q = q.Where(u => u.PropertyId == propertyId.Value);

        var total = await q.CountAsync();
        var byStatus = await q.GroupBy(u => u.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        int Get(string s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;

        var avgRent = total == 0 ? 0m : await q.AverageAsync(u => u.MonthlyRent);
        var occupancyPercent = total == 0 ? 0
            : Math.Round((Get(UnitStatuses.Occupied) * 100.0) / total, 1);

        return Json(new
        {
            generatedAt = DateTime.UtcNow,
            totalUnits = total,
            vacant = Get(UnitStatuses.Vacant),
            occupied = Get(UnitStatuses.Occupied),
            maintenance = Get(UnitStatuses.Maintenance),
            reserved = Get(UnitStatuses.Reserved),
            avgRent = Math.Round(avgRent, 3),
            occupancyPercent,
        });
    }

    /// <summary>
    /// Per-unit KPI batch — lifetime tenant count, months occupied,
    /// current-lease end, and last-tenant name. Mirrors the
    /// TenantKpiBatch/LeasesKpiBatch pattern.
    /// </summary>
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Client, VaryByQueryKeys = new[] { "ids" })]
    public async Task<IActionResult> KpiBatch(string? ids)
    {
        var idList = (ids ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .Where(n => n > 0).Distinct().ToList();
        if (idList.Count == 0) return Json(Array.Empty<object>());

        // Leases have no company filter — pin them to units the caller can see.
        var leases = await _context.Leases
            .Where(l => idList.Contains(l.UnitId)
                && _context.Properties.Any(p => p.PropertyId == l.Unit.PropertyId))
            .Select(l => new
            {
                l.UnitId,
                l.LeaseId,
                l.LeaseStartDate,
                l.LeaseEndDate,
                l.Status,
                TenantName = l.Tenant.FirstName + " " + l.Tenant.LastName,
            })
            .ToListAsync();

        var rows = idList.Select(id =>
        {
            var unitLeases = leases.Where(l => l.UnitId == id).ToList();
            var active = unitLeases.FirstOrDefault(l => l.Status == LeaseStatuses.Active);
            var mostRecent = unitLeases.OrderByDescending(l => l.LeaseStartDate).FirstOrDefault();
            return new
            {
                unitId = id,
                tenantCount = unitLeases.Select(l => l.TenantName).Distinct().Count(),
                currentTenant = active?.TenantName,
                currentLeaseId = active?.LeaseId,
                currentLeaseEnd = active?.LeaseEndDate,
                lastTenant = mostRecent?.TenantName,
                lastLeaseEnd = mostRecent?.LeaseEndDate,
            };
        });
        return Json(rows);
    }

    /// <summary>
    /// Inline status change — Vacant / Occupied are auto-managed, so this
    /// endpoint primarily exists for Maintenance / Reserved holds.
    /// Refusing to flip away from an Occupied row that still has an active
    /// lease (the daily job would just flip it back, and the operator
    /// almost certainly wanted to Terminate the lease).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(int id, string newStatus)
    {
        if (!UnitStatuses.All.Contains(newStatus))
            return BadRequest(new { error = "invalid status" });

        // ScopedUnits() → NotFound for another company's unit.
        var unit = await ScopedUnits().FirstOrDefaultAsync(u => u.UnitId == id);
        if (unit == null) return NotFound();

        // Refuse to mark a unit with an active lease as anything other than Occupied.
        if (newStatus != UnitStatuses.Occupied)
        {
            var hasActive = await _context.Leases.AnyAsync(l =>
                l.UnitId == id && l.Status == LeaseStatuses.Active);
            if (hasActive)
                return BadRequest(new { error = "Unit has an active lease — terminate the lease first." });
        }

        var oldStatus = unit.Status;
        unit.Status = newStatus;
        unit.ModifiedDate = DateTime.UtcNow;

        // When flipping to Vacant manually, stamp the available-from date so
        // it shows up correctly even outside the Terminate flow.
        if (newStatus == UnitStatuses.Vacant && oldStatus != UnitStatuses.Vacant)
            unit.AvailableFromDate ??= DateTime.UtcNow;

        await _audit.WriteAsync(
            entityType: "Unit",
            entityId: unit.UnitId,
            action: "StatusChanged",
            byUser: User.Identity?.Name ?? "system",
            payload: new { from = oldStatus, to = newStatus });
        await _context.SaveChangesAsync();
        return Json(new { success = true, status = newStatus });
    }

    /// <summary>
    /// Read-only per-unit page — specs, current lease, KPI strip, and
    /// the lease history is loaded inline. Reuses the existing audit
    /// records on the lease side for the timeline rows.
    /// </summary>
    public async Task<IActionResult> Details(int id)
    {
        var unit = await ScopedUnits()
            .Include(u => u.Property)
            .FirstOrDefaultAsync(u => u.UnitId == id);
        if (unit == null) return NotFound();

        ViewBag.Leases = await _context.Leases
            .Include(l => l.Tenant)
            .Where(l => l.UnitId == id)
            .OrderByDescending(l => l.LeaseStartDate)
            .ToListAsync();

        ViewBag.MaintenanceRequests = await _context.MaintenanceRequests
            .Where(m => m.UnitId == id)
            .OrderByDescending(m => m.RequestDate)
            .Take(5)
            .ToListAsync();

        return View(unit);
    }

    /// <summary>
    /// Full lease-history timeline — every tenant who ever lived in this
    /// unit, with move-in / move-out dates, lease durations, and gaps
    /// between tenants explicitly marked as vacancies.
    /// </summary>
    public async Task<IActionResult> History(int id)
    {
        var unit = await ScopedUnits()
            .Include(u => u.Property)
            .FirstOrDefaultAsync(u => u.UnitId == id);
        if (unit == null) return NotFound();

        var leases = await _context.Leases
            .Include(l => l.Tenant)
            .Where(l => l.UnitId == id)
            .OrderBy(l => l.LeaseStartDate)
            .ToListAsync();

        // Compute gaps between chronological leases — the vacancy windows
        // between tenants. Surfaced as their own rows in the view so
        // history reads as a continuous timeline.
        var rows = new List<UnitHistoryRow>();
        Lease? previous = null;
        foreach (var lease in leases)
        {
            if (previous != null)
            {
                var gapStart = previous.TerminationDate ?? previous.LeaseEndDate;
                var gapEnd = lease.LeaseStartDate;
                var gapDays = (gapEnd.Date - gapStart.Date).Days;
                if (gapDays > 1)
                {
                    rows.Add(new UnitHistoryRow
                    {
                        Kind = "Gap",
                        Start = gapStart.AddDays(1),
                        End = gapEnd.AddDays(-1),
                        Days = gapDays - 1,
                    });
                }
            }
            var endDate = lease.TerminationDate ?? lease.LeaseEndDate;
            rows.Add(new UnitHistoryRow
            {
                Kind = "Tenancy",
                Start = lease.LeaseStartDate,
                End = endDate,
                Days = (endDate.Date - lease.LeaseStartDate.Date).Days,
                LeaseId = lease.LeaseId,
                TenantName = lease.Tenant != null
                    ? $"{lease.Tenant.FirstName} {lease.Tenant.LastName}".Trim()
                    : "(unknown tenant)",
                LeaseStatus = lease.Status,
                MonthlyRent = lease.MonthlyRent,
            });
            previous = lease;
        }

        // Tail gap — if the last lease ended and the unit is still vacant.
        if (previous != null && unit.Status == UnitStatuses.Vacant)
        {
            var tailStart = previous.TerminationDate ?? previous.LeaseEndDate;
            var tailDays = (DateTime.Today - tailStart.Date).Days;
            if (tailDays > 1)
            {
                rows.Add(new UnitHistoryRow
                {
                    Kind = "Gap",
                    Start = tailStart.AddDays(1),
                    End = DateTime.Today,
                    Days = tailDays - 1,
                });
            }
        }

        rows.Reverse(); // newest first
        ViewBag.Unit = unit;
        return View(rows);
    }

    /// <summary>
    /// Excel export of the current filter. Filenames reflect the scope so
    /// "vacant.xlsx", "tower-b-2bhk.xlsx" etc. are distinguishable.
    /// </summary>
    public async Task<IActionResult> ExportExcel(int? propertyId, string? status,
        string? unitType, string? floor, string? sizeBand, string? search)
    {
        var q = ScopedUnits().Include(u => u.Property).AsQueryable();
        if (propertyId.HasValue) q = q.Where(u => u.PropertyId == propertyId.Value);
        if (!string.IsNullOrEmpty(status)) q = q.Where(u => u.Status == status);
        if (!string.IsNullOrEmpty(unitType)) q = q.Where(u => u.UnitType == unitType);
        if (!string.IsNullOrWhiteSpace(floor)) q = q.Where(u => u.FloorNumber == floor);
        if (!string.IsNullOrEmpty(sizeBand)) q = q.Where(u => u.SizeBand == sizeBand);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(u =>
                u.UnitNumber.ToLower().Contains(s) ||
                u.Property.PropertyName.ToLower().Contains(s));
        }

        var units = await q
            .OrderBy(u => u.Property.PropertyName)
            .ThenBy(u => u.FloorNumber)
            .ThenBy(u => u.UnitNumber)
            .ToListAsync();

        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("Units");
        var headers = new[] { "Property", "Unit #", "Floor", "Type", "Size band",
                              "Bedrooms", "Bathrooms", "Sq ft", "Status",
                              "Monthly Rent (KWD)", "Available from" };
        for (int c = 0; c < headers.Length; c++)
        {
            ws.Cells[1, c + 1].Value = headers[c];
            ws.Cells[1, c + 1].Style.Font.Bold = true;
            ws.Cells[1, c + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[1, c + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(99, 102, 241));
            ws.Cells[1, c + 1].Style.Font.Color.SetColor(System.Drawing.Color.White);
        }
        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i]; var r = i + 2;
            ws.Cells[r, 1].Value = u.Property?.PropertyName;
            ws.Cells[r, 2].Value = u.UnitNumber;
            ws.Cells[r, 3].Value = u.FloorNumber;
            ws.Cells[r, 4].Value = u.UnitType;
            ws.Cells[r, 5].Value = u.SizeBand;
            ws.Cells[r, 6].Value = u.Bedrooms;
            ws.Cells[r, 7].Value = u.Bathrooms;
            ws.Cells[r, 8].Value = u.SquareFeet;
            ws.Cells[r, 9].Value = u.Status;
            ws.Cells[r, 10].Value = u.MonthlyRent;
            ws.Cells[r, 10].Style.Numberformat.Format = "#,##0.000";
            ws.Cells[r, 11].Value = u.AvailableFromDate;
            ws.Cells[r, 11].Style.Numberformat.Format = "yyyy-mm-dd";
        }
        if (ws.Dimension != null) ws.Cells[ws.Dimension.Address].AutoFitColumns();

        var name = "units"
            + (propertyId.HasValue ? "-p" + propertyId : "")
            + (string.IsNullOrEmpty(status) ? "" : "-" + status.ToLowerInvariant())
            + "-" + DateTime.Today.ToString("yyyy-MM-dd") + ".xlsx";
        return File(pkg.GetAsByteArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    /// <summary>
    /// Bulk "Unit Setup" grid. Reached automatically after creating a property
    /// with <c>NumberOfUnits &gt; 0</c> (and from the property Edit page when the
    /// count is raised above the units that exist). Renders one editable row per
    /// unit still to be created — numbered <c>{prefix}{n}</c> starting just past
    /// any units the property already has — so a whole building is filled in and
    /// saved in one POST.
    /// </summary>
    public async Task<IActionResult> Setup(int propertyId, int? count, string? prefix)
    {
        var property = await _context.Properties
            .FirstOrDefaultAsync(p => p.PropertyId == propertyId);
        if (property == null) return NotFound();

        var existingCount = await _context.Units.CountAsync(u => u.PropertyId == propertyId);

        // Default target: however many units are still missing versus the
        // property's declared NumberOfUnits. Caller can override via ?count=.
        var toCreate = count ?? Math.Max(0, property.NumberOfUnits - existingCount);
        if (toCreate <= 0)
        {
            TempData["SuccessMessage"] =
                $"'{property.PropertyName}' already has all {existingCount} unit(s). Add more below if you need to.";
            toCreate = 1; // still let them add at least one ad-hoc unit
        }
        // Hard cap so a fat-fingered NumberOfUnits can't render thousands of rows.
        toCreate = Math.Min(toCreate, 200);

        var start = existingCount + 1;
        var vm = new UnitSetupViewModel
        {
            PropertyId = propertyId,
            PropertyName = property.PropertyName,
            Prefix = prefix,
            StartNumber = start,
            Units = Enumerable.Range(0, toCreate).Select(i => new UnitSetupRow
            {
                UnitNumber = $"{prefix}{start + i}"
            }).ToList()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Setup(UnitSetupViewModel vm)
    {
        var property = await _context.Properties
            .FirstOrDefaultAsync(p => p.PropertyId == vm.PropertyId);
        if (property == null) return NotFound();
        vm.PropertyName = property.PropertyName;

        // Drop rows the user blanked out entirely (removed via the grid).
        vm.Units = (vm.Units ?? new List<UnitSetupRow>())
            .Where(u => !string.IsNullOrWhiteSpace(u.UnitNumber))
            .ToList();

        if (vm.Units.Count == 0)
            ModelState.AddModelError(string.Empty, "Add at least one unit before saving.");

        // In-batch duplicate check (case-insensitive) so two rows can't claim
        // the same number before we ever hit the DB.
        var dupInBatch = vm.Units
            .GroupBy(u => u.UnitNumber.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        foreach (var d in dupInBatch)
            ModelState.AddModelError(string.Empty, $"Unit number '{d}' is used more than once in the grid.");

        // Collision with units already on the property (respects the
        // (PropertyId, UnitNumber) unique index).
        var existingNumbers = await _context.Units
            .Where(u => u.PropertyId == vm.PropertyId)
            .Select(u => u.UnitNumber)
            .ToListAsync();
        var existingSet = new HashSet<string>(existingNumbers, StringComparer.OrdinalIgnoreCase);
        foreach (var row in vm.Units)
            if (existingSet.Contains(row.UnitNumber.Trim()))
                ModelState.AddModelError(string.Empty,
                    $"Unit number '{row.UnitNumber.Trim()}' already exists on this property.");

        if (!ModelState.IsValid)
            return View(vm);

        var now = DateTime.UtcNow;
        foreach (var row in vm.Units)
        {
            var unit = new Unit
            {
                PropertyId = vm.PropertyId,
                UnitNumber = row.UnitNumber.Trim(),
                UnitType = row.UnitType ?? string.Empty,
                FloorNumber = row.FloorNumber,
                Bedrooms = row.Bedrooms,
                Bathrooms = row.Bathrooms,
                SquareFeet = row.SquareFeet,
                MonthlyRent = row.MonthlyRent,
                Status = UnitStatuses.Vacant,
                SizeBand = UnitSizeBands.Derive(row.Bedrooms, row.UnitType ?? string.Empty),
                CreatedDate = now
            };
            _context.Add(unit);
        }

        await _audit.WriteAsync(
            entityType: "Unit",
            entityId: vm.PropertyId,
            action: "BulkCreated",
            byUser: User.Identity?.Name ?? "system",
            payload: new { vm.PropertyId, Count = vm.Units.Count, Numbers = vm.Units.Select(u => u.UnitNumber.Trim()) });

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueIndexViolation(ex, "idx_units_property_number"))
        {
            // Race safety net — someone slipped a duplicate number in between
            // our checks and the save.
            ModelState.AddModelError(string.Empty,
                "One of the unit numbers was just taken by another change. Adjust the duplicates and save again.");
            return View(vm);
        }

        TempData["SuccessMessage"] = $"{vm.Units.Count} unit(s) created for '{property.PropertyName}'.";
        return RedirectToAction(nameof(Index), new { propertyId = vm.PropertyId });
    }

    public async Task<IActionResult> Create(int? propertyId)
    {
        await PopulateDropdownsAsync(propertyId);
        return View(new Unit { PropertyId = propertyId ?? 0, Status = UnitStatuses.Vacant });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("PropertyId,UnitNumber,UnitType,SquareFeet,Bathrooms,Bedrooms,MonthlyRent,FloorNumber")] Unit unit)
    {
        if (unit.PropertyId <= 0)
            ModelState.AddModelError(nameof(Unit.PropertyId), "Property is required.");
        if (string.IsNullOrWhiteSpace(unit.UnitNumber))
            ModelState.AddModelError(nameof(Unit.UnitNumber), "Unit number is required.");
        if (unit.MonthlyRent < 0)
            ModelState.AddModelError(nameof(Unit.MonthlyRent), "Rent cannot be negative.");

        // SECURITY: the property must belong to the caller's company. The
        // Properties DbSet is company-filtered, so a foreign PropertyId → NotFound.
        if (unit.PropertyId > 0
            && !await _context.Properties.AnyAsync(p => p.PropertyId == unit.PropertyId))
            return NotFound();

        // Proactive duplicate check against the (PropertyId, UnitNumber)
        // unique index. Without this the DB raises a SqlException that
        // bubbles up as a 500 — the user sees a stack trace, not a friendly
        // "that unit number is already taken" message.
        if (unit.PropertyId > 0 && !string.IsNullOrWhiteSpace(unit.UnitNumber))
        {
            var taken = await _context.Units.AnyAsync(u =>
                u.PropertyId == unit.PropertyId && u.UnitNumber == unit.UnitNumber);
            if (taken)
                ModelState.AddModelError(nameof(Unit.UnitNumber),
                    $"Unit number '{unit.UnitNumber}' already exists in this property. Pick a different number.");
        }

        if (ModelState.IsValid)
        {
            unit.Status = UnitStatuses.Vacant;
            unit.SizeBand = UnitSizeBands.Derive(unit.Bedrooms, unit.UnitType);
            unit.CreatedDate = DateTime.UtcNow;
            _context.Add(unit);

            try
            {
                await _context.SaveChangesAsync();

                // Audit AFTER the save so the entry carries the real UnitId
                // (it was previously written with entityId 0).
                await _audit.WriteAsync(
                    entityType: "Unit",
                    entityId: unit.UnitId,
                    action: "Created",
                    byUser: User.Identity?.Name ?? "system",
                    payload: new { unit.PropertyId, unit.UnitNumber, unit.UnitType });

                TempData["SuccessMessage"] = $"Unit '{unit.UnitNumber}' created.";
                return RedirectToAction(nameof(Details), new { id = unit.UnitId });
            }
            catch (DbUpdateException ex) when (IsUniqueIndexViolation(ex, "idx_units_property_number"))
            {
                // Race-condition safety net — another request slipped a duplicate
                // in between our pre-check and SaveChanges. Same friendly message.
                ModelState.AddModelError(nameof(Unit.UnitNumber),
                    $"Unit number '{unit.UnitNumber}' already exists in this property. Pick a different number.");
            }
        }
        await PopulateDropdownsAsync(unit.PropertyId);
        return View(unit);
    }

    /// <summary>
    /// Recognise the (PropertyId, UnitNumber) unique-index violation from
    /// SQL Server's error payload. The named-index check makes the recovery
    /// surgical — any other DbUpdateException still bubbles up.
    /// </summary>
    private static bool IsUniqueIndexViolation(DbUpdateException ex, string indexName)
    {
        if (ex.InnerException is not Microsoft.Data.SqlClient.SqlException sql) return false;
        if (sql.Number != 2601 && sql.Number != 2627) return false; // 2601=duplicate key, 2627=PK violation
        return sql.Message?.Contains(indexName, StringComparison.OrdinalIgnoreCase) == true;
    }

    public async Task<IActionResult> Edit(int id)
    {
        var unit = await ScopedUnits()
            .Include(u => u.Property)
            .FirstOrDefaultAsync(u => u.UnitId == id);
        if (unit == null) return NotFound();
        await PopulateDropdownsAsync(unit.PropertyId);
        return View(unit);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("UnitId,UnitNumber,UnitType,SquareFeet,Bathrooms,Bedrooms,MonthlyRent,FloorNumber,RowVersion")] Unit unit)
    {
        if (id != unit.UnitId) return NotFound();

        // The form still posts PropertyId / Status / CreatedDate as hidden
        // fields; they are intentionally unbound (Status changes go through
        // ChangeStatus, a unit is never moved between properties).
        ModelState.Remove(nameof(Unit.PropertyId));
        ModelState.Remove(nameof(Unit.Status));
        ModelState.Remove(nameof(Unit.CreatedDate));

        // LOAD-THEN-COPY. `_context.Update(postedModel)` marked EVERY column
        // modified, so AvailableFromDate / SizeBand / anything unposted was
        // overwritten with defaults. ScopedUnits() also enforces company scope.
        var existing = await ScopedUnits()
            .Include(u => u.Property)
            .FirstOrDefaultAsync(u => u.UnitId == id);
        if (existing == null) return NotFound();

        if (unit.MonthlyRent < 0)
            ModelState.AddModelError(nameof(Unit.MonthlyRent), "Rent cannot be negative.");

        // Same duplicate guard as Create — but skip the row being edited.
        // Always against the unit's REAL property, never a posted one.
        if (!string.IsNullOrWhiteSpace(unit.UnitNumber))
        {
            var taken = await _context.Units.AnyAsync(u =>
                u.PropertyId == existing.PropertyId
                && u.UnitNumber == unit.UnitNumber
                && u.UnitId != unit.UnitId);
            if (taken)
                ModelState.AddModelError(nameof(Unit.UnitNumber),
                    $"Unit number '{unit.UnitNumber}' already exists in this property. Pick a different number.");
        }

        if (ModelState.IsValid)
        {
            try
            {
                // Optimistic concurrency: compare against the RowVersion the
                // form was rendered with, not the one we just loaded.
                if (unit.RowVersion != null)
                    _context.Entry(existing).Property(u => u.RowVersion).OriginalValue = unit.RowVersion;

                existing.UnitNumber = unit.UnitNumber;
                existing.UnitType = unit.UnitType ?? string.Empty;
                existing.SquareFeet = unit.SquareFeet;
                existing.Bathrooms = unit.Bathrooms;
                existing.Bedrooms = unit.Bedrooms;
                existing.MonthlyRent = unit.MonthlyRent;
                existing.FloorNumber = unit.FloorNumber;
                existing.SizeBand = UnitSizeBands.Derive(existing.Bedrooms, existing.UnitType);
                existing.ModifiedDate = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Unit updated.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (DbUpdateConcurrencyException)
            {
                TempData["ErrorMessage"] = "Unit was changed by someone else — refresh and retry.";
                return RedirectToAction(nameof(Edit), new { id });
            }
            catch (DbUpdateException ex) when (IsUniqueIndexViolation(ex, "idx_units_property_number"))
            {
                ModelState.AddModelError(nameof(Unit.UnitNumber),
                    $"Unit number '{unit.UnitNumber}' already exists in this property. Pick a different number.");
            }
        }
        // Re-render with the real (unbound) property/status so the view stays honest.
        unit.PropertyId = existing.PropertyId;
        unit.Property = existing.Property;
        unit.Status = existing.Status;
        unit.CreatedDate = existing.CreatedDate;
        await PopulateDropdownsAsync(unit.PropertyId);
        return View(unit);
    }

    /// <summary>
    /// Units have no company query filter of their own. Restricting through the
    /// filtered Properties DbSet (a correlated EXISTS) yields only the caller's
    /// company's units; SuperAdmin sees all because the Property filter is bypassed.
    /// </summary>
    private IQueryable<Unit> ScopedUnits() =>
        _context.Units.Where(u => _context.Properties.Any(p => p.PropertyId == u.PropertyId));

    private async Task PopulateDropdownsAsync(int? selectedPropertyId)
    {
        var properties = await _context.Properties
            .OrderBy(p => p.PropertyName)
            .Select(p => new { p.PropertyId, p.PropertyName })
            .ToListAsync();
        ViewData["PropertyId"] = new SelectList(properties, "PropertyId", "PropertyName", selectedPropertyId);
        ViewData["UnitTypes"] = new SelectList(UnitTypes.All);
        ViewData["UnitStatuses"] = new SelectList(UnitStatuses.All);
    }

    /// <summary>One row in the unit's lifetime timeline — tenancy or gap.</summary>
    public class UnitHistoryRow
    {
        public string Kind { get; set; } = "Tenancy"; // or "Gap"
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public int Days { get; set; }
        public int? LeaseId { get; set; }
        public string? TenantName { get; set; }
        public string? LeaseStatus { get; set; }
        public decimal? MonthlyRent { get; set; }
    }
}
