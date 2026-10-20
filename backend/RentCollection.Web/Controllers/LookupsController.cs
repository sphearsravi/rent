using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;
using RentCollection.Web.Services;

namespace RentCollection.Web.Controllers;

/// <summary>
/// Admin CRUD for the system's dropdown values (classification categories) with
/// English + Arabic labels. One generic table backs every dropdown; you pick a
/// category and manage its options. Codes are stable (records store them), so
/// editing only changes labels/order/active — and system-seeded rows can be
/// relabelled/deactivated but not hard-deleted.
/// </summary>
[Authorize(Roles = Roles.AnyAdmin)]
public class LookupsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILookupService _lookups;

    public LookupsController(ApplicationDbContext context, ILookupService lookups)
    {
        _context = context;
        _lookups = lookups;
    }

    private static bool IsKnownCategory(string? category)
        => category != null && LookupCategories.All.Any(c => c.Key == category);

    public async Task<IActionResult> Index(string? category)
    {
        if (!IsKnownCategory(category)) category = LookupCategories.All[0].Key;
        ViewBag.Category = category;
        ViewBag.Categories = LookupCategories.All;

        var items = await _context.LookupItems.AsNoTracking()
            .Where(l => l.Category == category)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.LabelEn)
            .ToListAsync();
        return View(items);
    }

    [HttpGet]
    public IActionResult Create(string category)
    {
        if (!IsKnownCategory(category)) return RedirectToAction(nameof(Index));
        return View(new LookupItem { Category = category, IsActive = true, SortOrder = 99 });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LookupItem item)
    {
        if (!IsKnownCategory(item.Category)) return RedirectToAction(nameof(Index));
        if (string.IsNullOrWhiteSpace(item.Code))
            ModelState.AddModelError(nameof(item.Code), "Code is required.");
        if (string.IsNullOrWhiteSpace(item.LabelEn) && string.IsNullOrWhiteSpace(item.LabelAr))
            ModelState.AddModelError(nameof(item.LabelEn), "Provide at least an English or Arabic label.");

        item.Code = item.Code?.Trim() ?? string.Empty;
        if (ModelState.IsValid && await _context.LookupItems
                .AnyAsync(l => l.Category == item.Category && l.Code == item.Code))
            ModelState.AddModelError(nameof(item.Code), "That code already exists in this category.");

        if (!ModelState.IsValid) return View(item);

        _context.LookupItems.Add(new LookupItem
        {
            Category = item.Category,
            Code = item.Code,
            LabelEn = item.LabelEn?.Trim() ?? string.Empty,
            LabelAr = item.LabelAr?.Trim() ?? string.Empty,
            SortOrder = item.SortOrder,
            IsActive = true,
            IsSystem = false,
            CreatedDate = DateTime.UtcNow,
            CreatedBy = User.Identity?.Name,
        });
        await _context.SaveChangesAsync();
        _lookups.Invalidate(item.Category);
        TempData["LookupMsg"] = "Value added.";
        return RedirectToAction(nameof(Index), new { category = item.Category });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var item = await _context.LookupItems.FindAsync(id);
        if (item == null) return NotFound();
        return View(item);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, LookupItem input)
    {
        var item = await _context.LookupItems.FindAsync(id);
        if (item == null) return NotFound();

        if (string.IsNullOrWhiteSpace(input.LabelEn) && string.IsNullOrWhiteSpace(input.LabelAr))
            ModelState.AddModelError(nameof(input.LabelEn), "Provide at least an English or Arabic label.");
        if (!ModelState.IsValid) { input.Category = item.Category; input.Code = item.Code; input.IsSystem = item.IsSystem; return View(input); }

        // Code is immutable (records reference it); only labels/order/active change.
        item.LabelEn = input.LabelEn?.Trim() ?? string.Empty;
        item.LabelAr = input.LabelAr?.Trim() ?? string.Empty;
        item.SortOrder = input.SortOrder;
        item.IsActive = input.IsActive;
        item.ModifiedDate = DateTime.UtcNow;
        item.ModifiedBy = User.Identity?.Name;
        await _context.SaveChangesAsync();
        _lookups.Invalidate(item.Category);
        TempData["LookupMsg"] = "Value updated.";
        return RedirectToAction(nameof(Index), new { category = item.Category });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var item = await _context.LookupItems.FindAsync(id);
        if (item == null) return NotFound();
        item.IsActive = !item.IsActive;
        item.ModifiedDate = DateTime.UtcNow;
        item.ModifiedBy = User.Identity?.Name;
        await _context.SaveChangesAsync();
        _lookups.Invalidate(item.Category);
        TempData["LookupMsg"] = item.IsActive ? "Value activated." : "Value deactivated.";
        return RedirectToAction(nameof(Index), new { category = item.Category });
    }

    // Hard delete is allowed ONLY for custom (non-system) values. Seeded system
    // values can be deactivated but never deleted, so existing records keep their
    // labels.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _context.LookupItems.FindAsync(id);
        if (item == null) return NotFound();
        if (item.IsSystem)
        {
            TempData["LookupMsg"] = "System values can't be deleted — deactivate them instead.";
            return RedirectToAction(nameof(Index), new { category = item.Category });
        }
        var cat = item.Category;
        _context.LookupItems.Remove(item);
        await _context.SaveChangesAsync();
        _lookups.Invalidate(cat);
        TempData["LookupMsg"] = "Value deleted.";
        return RedirectToAction(nameof(Index), new { category = cat });
    }
}
