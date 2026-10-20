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
/// SuperAdmin-only — audit data is sensitive (who did what when), and
/// <see cref="AuditLog"/> is a GLOBAL table with no CompanyId column, so a
/// company Admin browsing it would see every other company's activity
/// (entity ids, usernames, payloads).
/// TODO: add CompanyId to AuditLog (stamp it in DbAuditLogService from the
/// caller's claim) and re-open this controller to Roles.AnyAdmin with a
/// company-scoped predicate.
/// </summary>
[Authorize(Roles = Roles.SuperAdmin)]
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
    public async Task<IActionResult> Index(
        [FromQuery] string? entityType,
        [FromQuery(Name = "action")] string? actionFilter,
        [FromQuery] string? byUser,
        [FromQuery] int take = 100)
    {
        // NOTE (bugfix): the filter parameter MUST be bound from the query string
        // only. It was previously named `action`, which collides with the default
        // route's `{action}` token — MVC bound the route's action name ("Index")
        // into it, so the feed always filtered `WHERE Action = 'Index'` and showed
        // nothing. [FromQuery(Name="action")] pins it to the ?action= query value.
        var q = _context.AuditLogs.AsQueryable();
        if (!string.IsNullOrEmpty(entityType))   q = q.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrEmpty(actionFilter)) q = q.Where(a => a.Action == actionFilter);
        if (!string.IsNullOrEmpty(byUser))       q = q.Where(a => a.ByUser == byUser);

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

        ViewBag.Filter = new { EntityType = entityType, Action = actionFilter, ByUser = byUser, Take = take };
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
