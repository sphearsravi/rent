using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.Controllers;

[Authorize(Roles = Roles.Operational)]
public class MaintenanceController : Controller
{
    private readonly ApplicationDbContext _context;

    public MaintenanceController(ApplicationDbContext context)
    {
        _context = context;
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
                .OrderByDescending(m => m.RequestDate)
                .ToListAsync();

            return View(requests);
        }
        catch (Exception)
        {
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
            .FirstOrDefaultAsync(m => m.RequestId == id);

        if (request == null)
        {
            return NotFound();
        }

        return View(request);
    }

    public async Task<IActionResult> Create()
    {
        ViewData["PropertyId"] = new SelectList(await _context.Properties.ToListAsync(), "PropertyId", "PropertyName");
        ViewData["UnitId"] = new SelectList(await _context.Units.ToListAsync(), "UnitId", "UnitNumber");
        ViewData["TenantId"] = new SelectList(await _context.Tenants.ToListAsync(), "TenantId", "FirstName");
        var users = await _context.Users.ToListAsync();
        ViewData["AssignedToId"] = new SelectList(users.Select(u => new { u.UserId, DisplayName = $"{u.FirstName} {u.LastName} ({u.Email})" }), "UserId", "DisplayName");
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("PropertyId,UnitId,TenantId,Category,Priority,Description,EstimatedCost,AssignedToUserId")] MaintenanceRequest request)
    {
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
        ViewData["PropertyId"] = new SelectList(await _context.Properties.ToListAsync(), "PropertyId", "PropertyName", request.PropertyId);
        ViewData["UnitId"] = new SelectList(await _context.Units.ToListAsync(), "UnitId", "UnitNumber", request.UnitId);
        ViewData["TenantId"] = new SelectList(await _context.Tenants.ToListAsync(), "TenantId", "FirstName", request.TenantId);
        var users = await _context.Users.ToListAsync();
        ViewData["AssignedToId"] = new SelectList(users.Select(u => new { u.UserId, DisplayName = $"{u.FirstName} {u.LastName} ({u.Email})" }), "UserId", "DisplayName", request.AssignedToUserId);
        return View(request);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var request = await _context.MaintenanceRequests.FindAsync(id);
        if (request == null)
        {
            return NotFound();
        }
        ViewData["PropertyId"] = new SelectList(await _context.Properties.ToListAsync(), "PropertyId", "PropertyName", request.PropertyId);
        ViewData["UnitId"] = new SelectList(await _context.Units.ToListAsync(), "UnitId", "UnitNumber", request.UnitId);
        ViewData["TenantId"] = new SelectList(await _context.Tenants.ToListAsync(), "TenantId", "FirstName", request.TenantId);
        var users = await _context.Users.ToListAsync();
        ViewData["AssignedToId"] = new SelectList(users.Select(u => new { u.UserId, DisplayName = $"{u.FirstName} {u.LastName} ({u.Email})" }), "UserId", "DisplayName", request.AssignedToUserId);
        return View(request);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("RequestId,PropertyId,UnitId,TenantId,Category,Priority,Description,EstimatedCost,Status,AssignedToUserId,CompletedDate,ActualCost,Notes,RequestDate")] MaintenanceRequest request)
    {
        if (id != request.RequestId)
        {
            return NotFound();
        }

        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(request);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Maintenance request updated successfully!";
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!MaintenanceRequestExists(request.RequestId))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }
            return RedirectToAction(nameof(Index));
        }
        ViewData["PropertyId"] = new SelectList(await _context.Properties.ToListAsync(), "PropertyId", "PropertyName", request.PropertyId);
        ViewData["UnitId"] = new SelectList(await _context.Units.ToListAsync(), "UnitId", "UnitNumber", request.UnitId);
        ViewData["TenantId"] = new SelectList(await _context.Tenants.ToListAsync(), "TenantId", "FirstName", request.TenantId);
        var users = await _context.Users.ToListAsync();
        ViewData["AssignedToId"] = new SelectList(users.Select(u => new { u.UserId, DisplayName = $"{u.FirstName} {u.LastName} ({u.Email})" }), "UserId", "DisplayName", request.AssignedToUserId);
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

        if (request == null) return NotFound();

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
        var request = await _context.MaintenanceRequests.FindAsync(id);
        if (request == null)
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

        if (request == null)
        {
            return NotFound();
        }

        return View(request);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var request = await _context.MaintenanceRequests.FindAsync(id);
        if (request != null)
        {
            _context.MaintenanceRequests.Remove(request);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Maintenance request deleted successfully!";
        }

        return RedirectToAction(nameof(Index));
    }

    private bool MaintenanceRequestExists(int id)
    {
        return _context.MaintenanceRequests.Any(e => e.RequestId == id);
    }
}
