using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.Controllers;

/// <summary>
/// Polymorphic audit-log viewer. One controller + view serves every
/// entity type that writes to <see cref="AuditLog"/> — Lease, Tenant,
/// User, Unit, Property, Document, Refund, etc. The entity-specific
/// history pages (e.g. /Leases/History/{id}) can keep their richer
/// presentation; this is the catch-all for entities that don't have
/// one yet, plus a global recent-activity feed.
///
/// Admin-only — audit data is sensitive (who did what when).
/// </summary>
[Authorize(Roles = Roles.AnyAdmin)]
public class AuditController : Controller
{
    private readonly ApplicationDbContext _context;

    public AuditController(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Global recent-activity feed across every entity type. Filter by
    /// EntityType / Action / ByUser via query string. Default = last 100
    /// events newest-first.
    /// </summary>
    public async Task<IActionResult> Index(string? entityType, string? action, string? byUser, int take = 100)
    {
        var q = _context.AuditLogs.AsQueryable();
        if (!string.IsNullOrEmpty(entityType)) q = q.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrEmpty(action))     q = q.Where(a => a.Action == action);
        if (!string.IsNullOrEmpty(byUser))     q = q.Where(a => a.ByUser == byUser);

        var rows = await q.OrderByDescending(a => a.At)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync();

        // Surface distinct values for the chip rows. Cheap (small cardinality).
        ViewBag.EntityTypes = await _context.AuditLogs
            .Select(a => a.EntityType).Distinct()
            .OrderBy(s => s).ToListAsync();
        ViewBag.Actions = await _context.AuditLogs
            .Select(a => a.Action).Distinct()
            .OrderBy(s => s).ToListAsync();

        ViewBag.Filter = new { EntityType = entityType, Action = action, ByUser = byUser, Take = take };
        return View(rows);
    }

    /// <summary>
    /// Per-entity timeline. URL form: <c>/Audit/{entityType}/{entityId}</c>
    /// matches the link pattern the Dashboard activity feed emits.
    /// </summary>
    [HttpGet("Audit/{entityType}/{entityId:int}")]
    public async Task<IActionResult> Entity(string entityType, int entityId)
    {
        var rows = await _context.AuditLogs
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .OrderByDescending(a => a.At)
            .ToListAsync();

        ViewBag.EntityType = entityType;
        ViewBag.EntityId = entityId;
        // Best-effort deep-link back to the entity's own page (Details).
        ViewBag.EntityHref = entityType switch
        {
            "Lease"    => $"/Leases/Details/{entityId}",
            "Tenant"   => $"/Tenants/Details/{entityId}",
            "Property" => $"/Properties/Details/{entityId}",
            "Unit"     => $"/Units/Details/{entityId}",
            "User"     => $"/Users/Details/{entityId}",
            "Document" => $"/Documents",   // no per-doc detail page
            "Refund"   => "/Refunds",
            _          => null,
        };
        return View(rows);
    }
}
