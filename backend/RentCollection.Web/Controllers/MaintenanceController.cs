using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.Controllers;

[Authorize(Roles = Roles.Operational)]
public class MaintenanceController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<MaintenanceController> _logger;

    public MaintenanceController(ApplicationDbContext context, ILogger<MaintenanceController> logger)
    {
        _context = context;
        _logger = logger;
    }

    // Company scope: MaintenanceRequest has no company query filter, but its
    // Property does. A request is accessible iff its PropertyId is visible
    // through the filtered Properties set (SuperAdmin bypasses the filter; a
    // scoped admin with no companyId claim matches nothing → deny).
    private Task<bool> CanAccessPropertyAsync(int propertyId) =>
        _context.Properties.AnyAsync(p => p.PropertyId == propertyId);

    /// <summary>
    /// Server-side scope + integrity checks for Create/Edit POSTs: the
    /// property must be the caller's, the unit (if any) must belong to that
    /// property, and the tenant (if any) must be visible through the filtered
    /// Tenants set. Adds ModelState errors; caller re-renders.
    /// </summary>
    private async Task ValidateScopeAsync(MaintenanceRequest request)
    {
        if (!await CanAccessPropertyAsync(request.PropertyId))
            ModelState.AddModelError(nameof(MaintenanceRequest.PropertyId), "Select a valid property.");
        if (request.UnitId.HasValue
            && !await _context.Units.AnyAsync(u => u.UnitId == request.UnitId.Value
                                                && u.PropertyId == request.PropertyId))
            ModelState.AddModelError(nameof(MaintenanceRequest.UnitId), "Unit does not belong to the selected property.");
        if (request.TenantId.HasValue
            && !await _context.Tenants.AnyAsync(t => t.TenantId == request.TenantId.Value))
            ModelState.AddModelError(nameof(MaintenanceRequest.TenantId), "Select a valid tenant.");
    }

    /// <summary>
    /// Shared dropdown builder for Create + Edit. Properties/Tenants are
    /// company-filtered by EF; Units are narrowed to those properties.
    /// AssignedTo: the FK (MaintenanceRequest.AssignedToUserId, string) targets
    /// ApplicationUser.Id, but this list used to be built from the int-keyed
    /// custom Users table — so any assignee picked could never satisfy the FK.
    /// Build it from ApplicationUser instead. The ViewData key stays
    /// "AssignedToId" because the views bind asp-items="ViewBag.AssignedToId".
    /// </summary>
    private async Task PopulateDropdownsAsync(
        int? propertyId = null, int? unitId = null, int? tenantId = null, string? assignedToUserId = null)
    {
        ViewData["PropertyId"] = new SelectList(
            await _context.Properties.OrderBy(p => p.PropertyName).ToListAsync(),
            "PropertyId", "PropertyName", propertyId);
        ViewData["UnitId"] = new SelectList(
            await _context.Units
                .Where(u => _context.Properties.Any(p => p.PropertyId == u.PropertyId))
                .OrderBy(u => u.UnitNumber)
                .ToListAsync(),
            "UnitId", "UnitNumber", unitId);
        ViewData["TenantId"] = new SelectList(
            await _context.Tenants.OrderBy(t => t.FirstName).ToListAsync(),
            "TenantId", "FirstName", tenantId);

        var scopedCompany = User.IsInRole(Roles.SuperAdmin) ? (int?)null
            : int.TryParse(User.FindFirst("companyId")?.Value, out var cid) ? cid : -1;
        var users = await _context.Set<ApplicationUser>()
            .Where(u => u.IsActive && (scopedCompany == null || u.CompanyId == scopedCompany))
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Select(u => new { u.Id, DisplayName = u.FirstName + " " + u.LastName + " (" + u.Email + ")" })
            .ToListAsync();
        ViewData["AssignedToId"] = new SelectList(users, "Id", "DisplayName", assignedToUserId);
    }

    public async Task<IActionResult> Index()
    {
        try
        {
            var requests = await _context.MaintenanceRequests
                .Include(m => m.Property)
                .Include(m => m.Unit)
                .Include(m => m.Tenant)
                .Include(m => m.AssignedToUser)
                // Company scope via the (filtered) Properties set.
                .Where(m => _context.Properties.Any(p => p.PropertyId == m.PropertyId))
                .OrderByDescending(m => m.RequestDate)
                .ToListAsync();

            return View(requests);
        }
        catch (Exception ex)
        {
            // Don't make a query failure look identical to "no requests".
            _logger.LogError(ex, "Error loading maintenance requests index");
            TempData["ErrorMessage"] = "Could not load maintenance requests. Please try again.";
            return View(new List<MaintenanceRequest>());
        }
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var request = await _context.MaintenanceRequests
            .Include(m => m.Property)
            .Include(m => m.Unit)
            .Include(m => m.Tenant)
            .Include(m => m.AssignedToUser)
            .Include(m => m.Photos)   // show the photos the tenant attached
            .FirstOrDefaultAsync(m => m.RequestId == id);

        if (request == null || !await CanAccessPropertyAsync(request.PropertyId))
        {
            return NotFound();
        }

        return View(request);
    }

    // Streams a maintenance photo (stored in the document store, keyed by
    // FilePath) so the admin Details page can display what the tenant attached.
    public async Task<IActionResult> Photo(int id, [FromServices] IDocumentStore store)
    {
        // Resolve the owning request's PropertyId in the same query so the
        // company check below can't be skipped by enumerating photo ids.
        var photo = await _context.MaintenancePhotos
            .AsNoTracking()
            .Where(p => p.MaintenancePhotoId == id)
            .Select(p => new { p.FilePath, p.ContentType, p.Request.PropertyId })
            .FirstOrDefaultAsync();
        if (photo == null || !await CanAccessPropertyAsync(photo.PropertyId)) return NotFound();

        var stream = await store.OpenReadAsync(photo.FilePath);
        if (stream == null) return NotFound();
        return File(stream, photo.ContentType ?? "application/octet-stream");
    }

    public async Task<IActionResult> Create()
    {
        await PopulateDropdownsAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("PropertyId,UnitId,TenantId,Category,Priority,Description,EstimatedCost,AssignedToUserId")] MaintenanceRequest request)
    {
        await ValidateScopeAsync(request);

        if (ModelState.IsValid)
        {
            request.RequestDate = DateTime.UtcNow;
            request.Status = "Open";
            // SLA clock: due date derived from priority. Overrideable via Edit.
            request.DueDate = request.RequestDate.AddHours(
                MaintenanceRequest.SlaHoursForPriority(request.Priority));
            _context.Add(request);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Maintenance request created successfully!";
            return RedirectToAction(nameof(Index));
        }
        await PopulateDropdownsAsync(request.PropertyId, request.UnitId, request.TenantId, request.AssignedToUserId);
        return View(request);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var request = await _context.MaintenanceRequests
            .Include(m => m.Property)
            .FirstOrDefaultAsync(m => m.RequestId == id);
        if (request == null || !await CanAccessPropertyAsync(request.PropertyId))
        {
            return NotFound();
        }
        await PopulateDropdownsAsync(request.PropertyId, request.UnitId, request.TenantId, request.AssignedToUserId);
        return View(request);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("RequestId,PropertyId,UnitId,TenantId,Category,Priority,Description,EstimatedCost,Status,AssignedToUserId,CompletedDate,ActualCost,Notes")] MaintenanceRequest request)
    {
        if (id != request.RequestId)
        {
            return NotFound();
        }

        // Load-then-copy: the old `_context.Update(request)` marked every
        // unbound column (DueDate, AssignedVendorId, CreatedDate, RequestDate)
        // as modified and overwrote them with defaults on each save.
        var existing = await _context.MaintenanceRequests
            .Include(m => m.Property)
            .FirstOrDefaultAsync(m => m.RequestId == id);
        if (existing == null || !await CanAccessPropertyAsync(existing.PropertyId))
        {
            return NotFound();
        }

        await ValidateScopeAsync(request);

        if (ModelState.IsValid)
        {
            try
            {
                existing.PropertyId = request.PropertyId;
                existing.UnitId = request.UnitId;
                existing.TenantId = request.TenantId;
                existing.Category = request.Category;
                existing.Priority = request.Priority;
                existing.Description = request.Description;
                existing.EstimatedCost = request.EstimatedCost;
                existing.Status = request.Status;
                existing.AssignedToUserId = string.IsNullOrWhiteSpace(request.AssignedToUserId)
                    ? null : request.AssignedToUserId;
                existing.CompletedDate = request.CompletedDate;
                existing.ActualCost = request.ActualCost;
                existing.Notes = request.Notes;
                existing.ModifiedDate = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Maintenance request updated successfully!";
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!MaintenanceRequestExists(request.RequestId))
                {
                    return NotFound();
                }
                TempData["ErrorMessage"] = "Request was changed by someone else — refresh and retry.";
                return RedirectToAction(nameof(Edit), new { id });
            }
            return RedirectToAction(nameof(Index));
        }
        await PopulateDropdownsAsync(request.PropertyId, request.UnitId, request.TenantId, request.AssignedToUserId);
        return View(request);
    }

    /// <summary>
    /// GET form for closing a maintenance request. Shows current request info
    /// plus inputs for optional Actual Cost / Completion Notes.
    /// </summary>
    public async Task<IActionResult> Close(int? id)
    {
        if (id == null) return NotFound();

        var request = await _context.MaintenanceRequests
            .Include(m => m.Property)
            .Include(m => m.Unit)
            .Include(m => m.Tenant)
            .FirstOrDefaultAsync(m => m.RequestId == id);

        if (request == null || !await CanAccessPropertyAsync(request.PropertyId)) return NotFound();

        if (request.Status == "Completed" || request.Status == "Cancelled")
        {
            TempData["InfoMessage"] = "Request is already closed.";
            return RedirectToAction(nameof(Details), new { id });
        }

        return View(request);
    }

    /// <summary>
    /// POST: flips Status to "Completed" and stamps CompletedDate.
    /// Optional actualCost/notes are persisted. Idempotent — re-submitting a
    /// Close on an already-Completed request is a no-op.
    /// </summary>
    [HttpPost, ActionName("Close")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseConfirmed(int id, decimal? actualCost, string? notes)
    {
        var request = await _context.MaintenanceRequests
            .Include(m => m.Property)
            .FirstOrDefaultAsync(m => m.RequestId == id);
        if (request == null || !await CanAccessPropertyAsync(request.PropertyId))
        {
            return NotFound();
        }

        if (request.Status == "Completed" || request.Status == "Cancelled")
        {
            TempData["InfoMessage"] = "Request is already closed.";
            return RedirectToAction(nameof(Details), new { id });
        }

        request.Status = "Completed";
        request.CompletedDate = DateTime.UtcNow;
        if (actualCost.HasValue) request.ActualCost = actualCost.Value;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            // Append rather than overwrite so existing notes are preserved.
            request.Notes = string.IsNullOrEmpty(request.Notes)
                ? notes
                : $"{request.Notes}\n[Closed {DateTime.UtcNow:yyyy-MM-dd HH:mm}] {notes}";
        }

        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Request #{id} closed.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var request = await _context.MaintenanceRequests
            .Include(m => m.Property)
            .Include(m => m.Unit)
            .Include(m => m.Tenant)
            .Include(m => m.AssignedToUser)
            .FirstOrDefaultAsync(m => m.RequestId == id);

        if (request == null || !await CanAccessPropertyAsync(request.PropertyId))
        {
            return NotFound();
        }

        return View(request);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var request = await _context.MaintenanceRequests
            .Include(m => m.Property)
            .FirstOrDefaultAsync(m => m.RequestId == id);
        if (request == null || !await CanAccessPropertyAsync(request.PropertyId))
        {
            return NotFound();
        }

        _context.MaintenanceRequests.Remove(request);
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = "Maintenance request deleted successfully!";

        return RedirectToAction(nameof(Index));
    }

    private bool MaintenanceRequestExists(int id)
    {
        return _context.MaintenanceRequests.Any(e => e.RequestId == id);
    }
}
