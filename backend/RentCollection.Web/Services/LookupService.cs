using System.Globalization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.Services;

/// <summary>
/// Reads the admin-managed dropdown values (<see cref="LookupItem"/>) for a
/// category, localized to the current UI culture, and caches them. Dropdowns
/// pull their options from here instead of hardcoded lists, so admins can add /
/// relabel / reorder / deactivate values without a code change.
/// </summary>
public interface ILookupService
{
    /// <summary>Active options for a category as (Code, localized Label), ordered.</summary>
    Task<IReadOnlyList<(string Code, string Label)>> GetOptionsAsync(string category);

    /// <summary>Ready-to-bind dropdown items. The already-selected value is always
    /// kept even if it's since been deactivated (so editing a record never loses it).</summary>
    Task<List<SelectListItem>> GetSelectListAsync(string category, string? selected = null);

    /// <summary>Localized label for a stored code (falls back to the code). Best-effort sync read.</summary>
    string LabelFor(string category, string? code);

    /// <summary>Drop the cache for a category after an edit.</summary>
    void Invalidate(string category);
}

public class LookupService : ILookupService
{
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;

    public LookupService(ApplicationDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    private static string Key(string category) => "lookup:" + category;
    private static bool IsArabic =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ar", StringComparison.OrdinalIgnoreCase);

    private async Task<List<LookupItem>> AllAsync(string category)
        => await _cache.GetOrCreateAsync(Key(category), async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            return await _db.LookupItems.AsNoTracking()
                .Where(l => l.Category == category)
                .OrderBy(l => l.SortOrder).ThenBy(l => l.LabelEn)
                .ToListAsync();
        }) ?? new List<LookupItem>();

    public async Task<IReadOnlyList<(string Code, string Label)>> GetOptionsAsync(string category)
    {
        var ar = IsArabic;
        var items = await AllAsync(category);
        return items.Where(i => i.IsActive).Select(i => (i.Code, i.Label(ar))).ToList();
    }

    public async Task<List<SelectListItem>> GetSelectListAsync(string category, string? selected = null)
    {
        var opts = await GetOptionsAsync(category);
        var list = opts
            .Select(o => new SelectListItem(o.Label, o.Code, o.Code == selected))
            .ToList();
        // Preserve a now-inactive selected value so an existing record keeps it.
        if (!string.IsNullOrEmpty(selected) && list.All(x => x.Value != selected))
            list.Insert(0, new SelectListItem(selected, selected, true));
        return list;
    }

    public string LabelFor(string category, string? code)
    {
        if (string.IsNullOrEmpty(code)) return string.Empty;
        if (_cache.TryGetValue(Key(category), out List<LookupItem>? items) && items != null)
        {
            var hit = items.FirstOrDefault(i => i.Code == code);
            if (hit != null) return hit.Label(IsArabic);
        }
        return code; // cache cold — fall back to the raw code
    }

    public void Invalidate(string category) => _cache.Remove(Key(category));
}
