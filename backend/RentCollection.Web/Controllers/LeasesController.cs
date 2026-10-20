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
using RentCollection.Web.Helpers;

namespace RentCollection.Web.Controllers;

[Authorize(Roles = Roles.Operational)]
public class LeasesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IAuditLogService _audit;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<LeasesController> _logger;

    public LeasesController(
        ApplicationDbContext context,
        IAuditLogService audit,
        IWebHostEnvironment environment,
        ILogger<LeasesController> logger)
    {
        _context = context;
        _audit = audit;
        _environment = environment;
        _logger = logger;
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    /// <summary>
    /// Closed-status helper used by Edit lock and view templates.
    /// LEASE_AUDIT Flaw #9 — Terminated/Renewed/Expired cannot be edited.
    /// </summary>
    private static bool IsClosed(string? status)
        => status == LeaseStatuses.Terminated
        || status == LeaseStatuses.Renewed
        || status == LeaseStatuses.Expired;

    /// <summary>
    /// A lease that still claims the unit: Active, or Pending (created with a
    /// deposit and waiting for Activate). Pending was the hole that let two
    /// leases land on one unit — only Active flipped the unit to Occupied and
    /// only Active is covered by UQ_Leases_UnitId_Active.
    /// </summary>
    private static bool IsLive(string? status)
        => status == LeaseStatuses.Active || status == LeaseStatuses.Pending;

    /// <summary>Shape returned by <see cref="LiveLeaseConflictAsync"/> for messages + the view.</summary>
    public sealed record LeaseConflict(int LeaseId, string Status, string TenantName, DateTime Start, DateTime End);

    /// <summary>
    /// The one rule that keeps a unit single-tenanted: no two LIVE leases may
    /// overlap on the same unit. Period-aware on purpose — a unit may be
    /// pre-leased for the day after the current lease ends (that is how
    /// Renew and Transfer work), but never for a period that intersects a
    /// live lease. The DB index is only a safety net for Active rows; this is
    /// the real guard for Create / Activate / Edit / Renew / Transfer.
    /// </summary>
    private async Task<LeaseConflict?> LiveLeaseConflictAsync(
        int unitId, DateTime start, DateTime end, int? excludeLeaseId = null)
    {
        var s = start.Date;
        var e = end.Date;
        return await _context.Leases
            .AsNoTracking()
            .Where(l => l.UnitId == unitId
                && (l.Status == LeaseStatuses.Active || l.Status == LeaseStatuses.Pending)
                && (excludeLeaseId == null || l.LeaseId != excludeLeaseId)
                && l.LeaseStartDate.Date <= e
                && l.LeaseEndDate.Date >= s)
            .OrderBy(l => l.LeaseStartDate)
            .Select(l => new LeaseConflict(
                l.LeaseId, l.Status,
                (l.Tenant.FirstName + " " + l.Tenant.LastName).Trim(),
                l.LeaseStartDate, l.LeaseEndDate))
            .FirstOrDefaultAsync();
    }

    private static string Article(string status) =>
        "aeiou".Contains(char.ToLowerInvariant(status[0])) ? "an" : "a";

    private static string DescribeConflict(LeaseConflict c) =>
        $"This unit already has {Article(c.Status)} {c.Status.ToLowerInvariant()} lease (#{c.LeaseId}, {c.TenantName}, " +
        $"{c.Start:dd MMM yyyy} – {c.End:dd MMM yyyy}) for that period. " +
        "Terminate or renew the existing lease, choose a start date after it ends, or pick another unit.";

    // Company scope: Lease has no company query filter (only Property/Tenant/
    // User/LegalCase do), so a raw LeaseId lookup is cross-company. Scope list
    // queries and guard every mutating action via the owning Tenant's CompanyId.
    // SuperAdmin is cross-company by design; a scoped admin with NO companyId
    // claim resolves to -1 (deny) rather than "no filter".
    private int? ScopedCompanyId() =>
        User.IsInRole(Roles.SuperAdmin) ? null
        : int.TryParse(User.FindFirst("companyId")?.Value, out var cid) ? cid : -1;

    private bool CanAccessCompany(int? entityCompanyId)
    {
        if (User.IsInRole(Roles.SuperAdmin)) return true;
        return int.TryParse(User.FindFirst("companyId")?.Value, out var cid)
            && entityCompanyId == cid;
    }

    /// <summary>
    /// The filtered unique index UQ_Leases_UnitId_Active rejects a second
    /// Active lease on the same unit. Surface that as a user message instead
    /// of a 500.
    /// </summary>
    private static bool IsActiveLeaseUniqueViolation(DbUpdateException ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
            if (e.Message.Contains("UQ_Leases_UnitId_Active", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    #region Leases landing — Sprint 1 endpoints

    /// <summary>
    /// Portfolio KPI strip for /Leases. Mirrors Tenants/Snapshot shape so
    /// the view can reuse the same skeleton-shimmer pattern. Cached for
    /// 30 s — list page reloads cheap.
    /// </summary>
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> Snapshot()
    {
        var today = DateTime.Today;
        var soon = today.AddDays(30);

        // Company-scoped base set (see ScopedCompanyId). Cached per-client only —
        // a shared/proxy cache would serve one company's numbers to another.
        var scope = ScopedCompanyId();
        var leases = _context.Leases
            .Where(l => scope == null || l.Tenant.CompanyId == scope);

        var totalLeases = await leases.CountAsync();
        var activeLeases = await leases.CountAsync(l => l.Status == LeaseStatuses.Active);

        // Expiring soon = Active AND end ≤ today+30 AND end ≥ today.
        var expiringSoon = await leases.CountAsync(l =>
            l.Status == LeaseStatuses.Active &&
            l.LeaseEndDate >= today &&
            l.LeaseEndDate <= soon);

        // Expired-but-still-Active = lease past end date but never closed.
        // These usually need the operator to renew or terminate.
        var expired = await leases.CountAsync(l =>
            l.Status == LeaseStatuses.Active && l.LeaseEndDate < today);

        // Monthly rent roll from Active leases.
        var monthlyRentRoll = await leases
            .Where(l => l.Status == LeaseStatuses.Active)
            .SumAsync(l => (decimal?)l.MonthlyRent) ?? 0m;

        return Json(new
        {
            generatedAt = DateTime.UtcNow,
            totalLeases,
            activeLeases,
            expiringSoon,
            expired,
            monthlyRentRoll,
        });
    }

    /// <summary>
    /// Excel export. Filter params mirror the view's filters
    /// (search / propertyId / status / expiring=N-days).
    /// </summary>
    public async Task<IActionResult> ExportExcel(
        string? search, int? propertyId, string? status, int? expiring)
    {
        var scope = ScopedCompanyId();
        var q = _context.Leases
            .Include(l => l.Tenant)
            .Include(l => l.Unit).ThenInclude(u => u.Property)
            .Where(l => scope == null || l.Tenant.CompanyId == scope)
            .AsQueryable();

        if (!string.IsNullOrEmpty(status))
            q = q.Where(l => l.Status == status);

        // propertyId is matched on the property of the lease's unit.
        if (propertyId.HasValue)
            q = q.Where(l => l.Unit.Property.PropertyId == propertyId.Value);

        if (expiring.HasValue && expiring.Value > 0)
        {
            var today = DateTime.Today;
            var horizon = today.AddDays(expiring.Value);
            q = q.Where(l => l.Status == LeaseStatuses.Active
                && l.LeaseEndDate >= today
                && l.LeaseEndDate <= horizon);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(l =>
                ((l.Tenant.FirstName ?? "") + " " + (l.Tenant.LastName ?? "")).ToLower().Contains(s) ||
                (l.Tenant.Email ?? "").ToLower().Contains(s) ||
                (l.Tenant.PACINumber ?? "").ToLower().Contains(s) ||
                (l.Unit.UnitNumber ?? "").ToLower().Contains(s) ||
                (l.Unit.Property.PropertyName ?? "").ToLower().Contains(s));
        }

        var leases = await q
            .OrderBy(l => l.Unit.Property.PropertyName)
            .ThenBy(l => l.Unit.UnitNumber)
            .ToListAsync();

        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("Leases");
        var headers = new[]
        {
            "Lease ID", "Property", "Unit", "Tenant", "PACI",
            "Start", "End", "Days Remaining", "Monthly Rent", "Status"
        };
        for (int c = 0; c < headers.Length; c++)
        {
            ws.Cells[1, c + 1].Value = headers[c];
            ws.Cells[1, c + 1].Style.Font.Bold = true;
            ws.Cells[1, c + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[1, c + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(99, 102, 241));
            ws.Cells[1, c + 1].Style.Font.Color.SetColor(System.Drawing.Color.White);
        }
        for (int i = 0; i < leases.Count; i++)
        {
            var l = leases[i]; var r = i + 2;
            ws.Cells[r, 1].Value = l.LeaseId;
            ws.Cells[r, 2].Value = l.Unit?.Property?.PropertyName;
            ws.Cells[r, 3].Value = l.Unit?.UnitNumber;
            ws.Cells[r, 4].Value = $"{l.Tenant?.FirstName} {l.Tenant?.LastName}".Trim();
            ws.Cells[r, 5].Value = l.Tenant?.PACINumber;
            ws.Cells[r, 6].Value = l.LeaseStartDate;
            ws.Cells[r, 6].Style.Numberformat.Format = "yyyy-mm-dd";
            ws.Cells[r, 7].Value = l.LeaseEndDate;
            ws.Cells[r, 7].Style.Numberformat.Format = "yyyy-mm-dd";
            ws.Cells[r, 8].Value = (l.LeaseEndDate - DateTime.Today).Days;
            ws.Cells[r, 9].Value = l.MonthlyRent;
            ws.Cells[r, 9].Style.Numberformat.Format = "#,##0.000";
            ws.Cells[r, 10].Value = l.Status;
        }
        if (ws.Dimension != null) ws.Cells[ws.Dimension.Address].AutoFitColumns();
        var fileName = $"leases-{DateTime.Today:yyyy-MM-dd}.xlsx";
        return File(pkg.GetAsByteArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    /// <summary>
    /// Per-lease KPI batch for the /Leases landing — outstanding balance,
    /// month-to-date collected, last payment date. One round-trip for all
    /// visible row ids. Mirrors the Tenants/KpiBatch shape.
    /// </summary>
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Client, VaryByQueryKeys = new[] { "ids" })]
    public async Task<IActionResult> KpiBatch(string? ids)
    {
        var idList = (ids ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .Where(n => n > 0).Distinct().ToList();
        if (idList.Count == 0) return Json(Array.Empty<object>());

        var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        // Company scope — ids arrive from the client, so a scoped admin could
        // otherwise probe another company's lease balances by id.
        var scope = ScopedCompanyId();

        // Outstanding = sum of (TotalAmount − PaidAmount) over unpaid invoices
        // grouped by lease. Matches the same definition used on /Invoices.
        var outstandingRaw = await _context.Invoices
            .Where(i => idList.Contains(i.LeaseId)
                        && (scope == null || i.Tenant.CompanyId == scope)
                        && i.Status != "Paid" && i.TotalAmount - i.PaidAmount > 0)
            .GroupBy(i => i.LeaseId)
            .Select(g => new { LeaseId = g.Key, Outstanding = g.Sum(i => i.TotalAmount - i.PaidAmount) })
            .ToListAsync();

        // Month-to-date collected = sum of completed payments whose date is
        // in the current calendar month, grouped by lease.
        var mtdRaw = await _context.Payments
            .Where(p => idList.Contains(p.LeaseId)
                        && (scope == null || p.Tenant.CompanyId == scope)
                        && p.Status == PaymentStatus.Completed
                        && p.PaymentDate >= monthStart)
            .GroupBy(p => p.LeaseId)
            .Select(g => new { LeaseId = g.Key, Mtd = g.Sum(p => p.AmountPaid) })
            .ToListAsync();

        // Last payment date over the lifetime of the lease — drives the
        // "Last paid" sub-label and the per-row health colour.
        var lastRaw = await _context.Payments
            .Where(p => idList.Contains(p.LeaseId)
                        && (scope == null || p.Tenant.CompanyId == scope)
                        && p.Status == PaymentStatus.Completed)
            .GroupBy(p => p.LeaseId)
            .Select(g => new { LeaseId = g.Key, LastDate = g.Max(p => p.PaymentDate) })
            .ToListAsync();

        // Invoice count — a lease with no invoices yet is "not billed", not
        // "paid up"; the list used to show the latter for brand-new leases.
        var invoiceCountRaw = await _context.Invoices
            .Where(i => idList.Contains(i.LeaseId)
                        && (scope == null || i.Tenant.CompanyId == scope))
            .GroupBy(i => i.LeaseId)
            .Select(g => new { LeaseId = g.Key, Count = g.Count() })
            .ToListAsync();

        var outstandingMap = outstandingRaw.ToDictionary(x => x.LeaseId, x => x.Outstanding);
        var mtdMap = mtdRaw.ToDictionary(x => x.LeaseId, x => x.Mtd);
        var lastMap = lastRaw.ToDictionary(x => x.LeaseId, x => x.LastDate);
        var countMap = invoiceCountRaw.ToDictionary(x => x.LeaseId, x => x.Count);

        var rows = idList.Select(id => new
        {
            leaseId = id,
            outstanding = outstandingMap.GetValueOrDefault(id, 0m),
            mtdCollected = mtdMap.GetValueOrDefault(id, 0m),
            lastPaymentDate = lastMap.TryGetValue(id, out var d) ? d : (DateTime?)null,
            invoiceCount = countMap.GetValueOrDefault(id, 0),
        });
        return Json(rows);
    }

    #endregion

    public async Task<IActionResult> Index()
    {
        try
        {
            var scope = ScopedCompanyId();
            var leases = await _context.Leases
                .Include(l => l.Tenant)
                .Include(l => l.Unit)
                    .ThenInclude(u => u.Property)
                .Where(l => scope == null || l.Tenant.CompanyId == scope)
                .OrderByDescending(l => l.CreatedDate)
                .ToListAsync();

            // Latest legal case per lease → drives the ⚖️ badge in the list.
            // The LegalCases table is tiny, so load it whole and map by lease.
            var cases = await _context.LegalCases.AsNoTracking()
                .OrderByDescending(c => c.FiledDate).ThenByDescending(c => c.LegalCaseId)
                .ToListAsync();
            ViewBag.CaseByLease = cases
                .GroupBy(c => c.LeaseId)
                .ToDictionary(g => g.Key, g => g.First());

            return View(leases);
        }
        catch (Exception ex)
        {
            // Don't silently show an empty list — a DB/query failure here used to
            // look identical to "no leases". Log it so it's diagnosable.
            _logger.LogError(ex, "Error loading leases index");
            return View(new List<RentCollection.Core.Entities.Lease>());
        }
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var lease = await _context.Leases
            .Include(l => l.Tenant)
            .Include(l => l.Unit)
                .ThenInclude(u => u.Property)
            .Include(l => l.Payments)
            .Include(l => l.Invoices)
            .FirstOrDefaultAsync(m => m.LeaseId == id);

        if (lease == null || !CanAccessCompany(lease.Tenant?.CompanyId))
        {
            return NotFound();
        }

        // Recent activity preview — top 3 audit rows for the Details "Recent
        // activity" card. Full timeline lives at /Leases/History/{id}.
        ViewBag.RecentEvents = await _context.AuditLogs
            .Where(a => a.EntityType == "Lease" && a.EntityId == lease.LeaseId)
            .OrderByDescending(a => a.At)
            .Take(3)
            .ToListAsync();

        // Legal case (if any) filed on this lease — most recent first. Drives the
        // ⚖️ badge + the add/edit case panel on the Details page.
        ViewBag.LegalCase = await _context.LegalCases
            .Where(c => c.LeaseId == lease.LeaseId)
            .OrderByDescending(c => c.FiledDate)
            .ThenByDescending(c => c.LegalCaseId)
            .FirstOrDefaultAsync();

        // LEASE_AUDIT Flaw #10 — Details now hosts the Transfer form. Load
        // vacant units server-side so the dropdown doesn't depend on an
        // async fetch that might 404 or be blocked by auth.
        ViewBag.VacantUnits = await _context.Units
            .Include(u => u.Property)
            .Where(u => u.Status == "Vacant" && u.UnitId != lease.UnitId)
            .OrderBy(u => u.Property.PropertyName)
            .ThenBy(u => u.UnitNumber)
            .Select(u => new
            {
                UnitId = u.UnitId,
                Label = u.Property.PropertyName + " · Unit " + u.UnitNumber,
            })
            .ToListAsync();

        return View(lease);
    }

    /// <summary>
    /// Lease history page — combines the renewal chain (predecessors and
    /// successors, transitive) with all AuditLog rows for every lease in
    /// that chain. Backfilled rows are flagged <c>"synthesised": true</c>
    /// in their payload so the view can mark them as approximate.
    ///
    /// Query: <c>?filter=lifecycle|edits|all</c> (default = all).
    /// </summary>
    public async Task<IActionResult> History(int id, string? filter = null)
    {
        var lease = await _context.Leases
            .Include(l => l.Tenant)
            .Include(l => l.Unit).ThenInclude(u => u.Property)
            .FirstOrDefaultAsync(l => l.LeaseId == id);
        if (lease == null || !CanAccessCompany(lease.Tenant?.CompanyId)) return NotFound();

        // Walk the renewal chain in both directions. Bounded: a lease can
        // only have one RenewedFrom and one RenewedTo so the chain is a
        // strict linear sequence — but we cap iterations defensively in
        // case bad data introduces a cycle.
        var chainIds = new List<int> { lease.LeaseId };
        const int MaxHops = 50;

        var cursor = lease.RenewedFromLeaseId;
        int hops = 0;
        while (cursor.HasValue && hops++ < MaxHops)
        {
            var prev = await _context.Leases
                .Where(l => l.LeaseId == cursor.Value)
                .Select(l => new { l.LeaseId, l.RenewedFromLeaseId })
                .FirstOrDefaultAsync();
            if (prev == null) break;
            chainIds.Insert(0, prev.LeaseId);
            cursor = prev.RenewedFromLeaseId;
        }

        cursor = lease.RenewedToLeaseId;
        hops = 0;
        while (cursor.HasValue && hops++ < MaxHops)
        {
            var next = await _context.Leases
                .Where(l => l.LeaseId == cursor.Value)
                .Select(l => new { l.LeaseId, l.RenewedToLeaseId })
                .FirstOrDefaultAsync();
            if (next == null) break;
            chainIds.Add(next.LeaseId);
            cursor = next.RenewedToLeaseId;
        }

        // Render-ready chain tile data (lease id, status, period, current?).
        var chainTiles = await _context.Leases
            .Where(l => chainIds.Contains(l.LeaseId))
            .Select(l => new ChainTile(
                l.LeaseId,
                l.Status,
                l.LeaseStartDate,
                l.LeaseEndDate,
                l.LeaseId == id))
            .ToListAsync();
        // Preserve chronological chain order, not DB order.
        chainTiles = chainTiles.OrderBy(t => chainIds.IndexOf(t.LeaseId)).ToList();

        // Pull every AuditLog row whose EntityId is in the chain. One round
        // trip; the view groups them by lease id for the rendering pass.
        var events = await _context.AuditLogs
            .Where(a => a.EntityType == "Lease" && chainIds.Contains(a.EntityId))
            .OrderByDescending(a => a.At)
            .ToListAsync();

        // Filter chip handling.
        if (filter == "lifecycle")
            events = events.Where(e => e.Action != "Edited").ToList();
        else if (filter == "edits")
            events = events.Where(e => e.Action == "Edited").ToList();

        ViewBag.Chain = chainTiles;
        ViewBag.Filter = filter ?? "all";
        ViewBag.Lease = lease;
        return View(events);
    }

    /// <summary>
    /// Minimal record for the chain bar in the History view. Public so the
    /// .cshtml can declare its ViewBag cast cleanly.
    /// </summary>
    public record ChainTile(int LeaseId, string Status, DateTime Start, DateTime End, bool IsCurrent);

    /// <summary>
    /// Accepts an optional <c>unitId</c> query param so deep links from a
    /// Unit's Details page (`/Units/Details/{id}` → "Create lease") drop the
    /// user into a form pre-populated with the unit, its building, and the
    /// unit's default Monthly Rent. They still fill Tenant + dates + insurance,
    /// but the click-count drops from "browse two cascades" to "pick a tenant."
    /// </summary>
    public async Task<IActionResult> Create(int? unitId = null)
    {
        Lease? prefill = null;
        int? selectedPropertyId = null;

        if (unitId.HasValue && unitId.Value > 0)
        {
            var unit = await _context.Units
                .Where(u => u.UnitId == unitId.Value)
                .Select(u => new { u.UnitId, u.PropertyId, u.MonthlyRent, u.Status })
                .FirstOrDefaultAsync();

            if (unit != null)
            {
                selectedPropertyId = unit.PropertyId;
                prefill = new Lease
                {
                    UnitId = unit.UnitId,
                    MonthlyRent = unit.MonthlyRent,            // copy from the unit; user can edit
                    LeaseStartDate = DateTime.Today,
                    // A one-year term ends the day BEFORE the anniversary
                    // (13 Oct 2026 → 12 Oct 2027), not on it.
                    LeaseEndDate = DateTime.Today.AddYears(1).AddDays(-1),
                    PaymentDueDay = 1,                         // most common
                    LeaseType = "Fixed",
                    InsuranceFrequency = "None",
                };

                // Deep link onto a unit that is still claimed by a live lease:
                // say so up front, with the lease, so the operator renews /
                // terminates that one instead of discovering it on submit.
                var live = await LiveLeaseConflictAsync(unit.UnitId, DateTime.Today, DateTime.Today.AddYears(1));
                if (live != null)
                    TempData["InfoMessage"] =
                        $"Unit already has {Article(live.Status)} {live.Status.ToLowerInvariant()} lease (#{live.LeaseId}, {live.TenantName}, " +
                        $"until {live.End:dd MMM yyyy}). A new lease can only start after it ends — or use Renew / Terminate on that lease.";
                else if (!string.Equals(unit.Status, "Vacant", StringComparison.OrdinalIgnoreCase))
                    TempData["InfoMessage"] =
                        $"Unit is marked {unit.Status}. Creating a lease will mark it Occupied.";
            }
        }
        else
        {
            prefill = new Lease
            {
                LeaseStartDate = DateTime.Today,
                LeaseEndDate = DateTime.Today.AddYears(1).AddDays(-1),
                PaymentDueDay = 1,
                LeaseType = "Fixed",
                InsuranceFrequency = "None",
            };
        }

        await PopulateCreateEditDropdownsAsync(
            selectedUnitId: prefill?.UnitId,
            selectedPropertyId: selectedPropertyId,
            includeAllUnits: true);
        return View(prefill ?? new Lease());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("TenantId,UnitId,LeaseStartDate,LeaseEndDate,MonthlyRent,SecurityDeposit,PaymentDueDay,LeaseType,LateFeeAmount,LateFeeGracePeriod,InsuranceAmount,InsuranceFrequency,InsuranceStartDate")] Lease lease,
        List<IFormFile>? Documents = null,
        List<string>? DocumentTypes = null,
        [FromForm(Name = "__SaveAndAddAnother")] string? saveAndAddAnother = null)
    {
        // Status is set by the controller, never the form.
        //
        // Decision (deposit + insurance plan): when SecurityDeposit > 0 the
        // lease lands as Pending so the operator can issue + collect the
        // deposit invoice before money starts flowing as rent. Zero deposit
        // goes straight to Active. The Activate action below performs the
        // Pending → Active flip and issues the deposit invoice atomically.
        lease.Status = lease.SecurityDeposit > 0
            ? LeaseStatuses.Pending
            : LeaseStatuses.Active;

        ValidateLeaseFields(lease);

        // Company scope: both the tenant and the unit's property must be visible
        // through the filtered Tenants / Properties sets (SuperAdmin sees all).
        // Without this a scoped admin could POST another company's ids.
        if (!await _context.Tenants.AnyAsync(t => t.TenantId == lease.TenantId))
            ModelState.AddModelError(nameof(Lease.TenantId), "Select a valid tenant.");
        if (!await _context.Units.AnyAsync(u => u.UnitId == lease.UnitId
                && _context.Properties.Any(p => p.PropertyId == u.PropertyId)))
            ModelState.AddModelError(nameof(Lease.UnitId), "Select a valid unit.");

        // One tenant per unit per period. This is what was missing: a unit
        // with a Pending (deposit-awaiting) lease stayed "Vacant", stayed in
        // the dropdown, and happily took a second lease.
        if (ModelState.IsValid)
        {
            var conflict = await LiveLeaseConflictAsync(lease.UnitId, lease.LeaseStartDate, lease.LeaseEndDate);
            if (conflict != null)
            {
                ModelState.AddModelError(nameof(Lease.UnitId), DescribeConflict(conflict));
                ViewBag.Conflict = conflict;
            }
        }

        if (ModelState.IsValid)
        {
            lease.CreatedDate = DateTime.UtcNow;
            _context.Add(lease);

            // Sprint 13 — atomically flip the unit to Occupied when the
            // lease lands as Active. The daily reconcile-unit-status job
            // catches drift eventually but the user-facing list pages read
            // Unit.Status directly, so without this update the new lease's
            // unit shows as Vacant until 01:30 UTC the next day. Skip
            // Maintenance/Reserved units (operator-set states the job also
            // honours) so we don't trample an intentional hold.
            if (lease.Status == LeaseStatuses.Active)
            {
                var unit = await _context.Units.FindAsync(lease.UnitId);
                if (unit != null
                    && unit.Status != "Maintenance"
                    && unit.Status != "Reserved")
                {
                    unit.Status = "Occupied";
                    unit.ModifiedDate = DateTime.UtcNow;
                }
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsActiveLeaseUniqueViolation(ex))
            {
                // UQ_Leases_UnitId_Active — the "Vacant" dropdown filter is a
                // TOCTOU check; the DB index is the safety net. Show a message,
                // not a 500. Detach so the failed insert doesn't linger in the
                // change tracker for the re-render's dropdown queries.
                _context.Entry(lease).State = EntityState.Detached;
                TempData["ErrorMessage"] = "This unit already has an active lease.";
                var failedPropertyId = await _context.Units
                    .Where(u => u.UnitId == lease.UnitId)
                    .Select(u => (int?)u.PropertyId)
                    .FirstOrDefaultAsync();
                await PopulateCreateEditDropdownsAsync(lease.TenantId, lease.UnitId, failedPropertyId);
                return View(lease);
            }

            // Sprint 20 — attach any documents uploaded inline on the Create
            // form. Same pattern as Tenant + Property forms (signed lease PDF,
            // tenant insurance certificate, deposit receipt scan, etc.).
            if (Documents != null && Documents.Any() && DocumentTypes != null)
            {
                await UploadDocuments(lease.LeaseId, Documents, DocumentTypes, "Lease");
            }

            TempData["SuccessMessage"] = lease.Status == LeaseStatuses.Pending
                ? $"Lease created as Pending — issue the deposit invoice via Activate when ready."
                : "Lease created and Activated.";

            // Sprint 23 — "Save & add another" returns to a fresh Create form.
            if (string.Equals(saveAndAddAnother, "true", StringComparison.OrdinalIgnoreCase))
            {
                TempData["LeaseCreated"] = $"#{lease.LeaseId}";
                return RedirectToAction(nameof(Create));
            }
            return RedirectToAction(nameof(Index));
        }

        // Sprint 11 — derive PropertyId from the submitted UnitId so the
        // cascade re-renders correctly on validation failure.
        var selectedPropertyId = lease.UnitId > 0
            ? await _context.Units
                .Where(u => u.UnitId == lease.UnitId)
                .Select(u => (int?)u.PropertyId)
                .FirstOrDefaultAsync()
            : null;
        await PopulateCreateEditDropdownsAsync(lease.TenantId, lease.UnitId, selectedPropertyId);
        return View(lease);
    }

    /// <summary>
    /// Pending → Active activation. Issues the SecurityDeposit invoice
    /// atomically when the lease has one. Idempotency: <c>DepositInvoiced</c>
    /// flag prevents re-issuing if the action is re-triggered. Audit log
    /// + RowVersion concurrency, same posture as Renew/Terminate/Transfer.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.AnyAdmin)]
    public async Task<IActionResult> Activate(int id)
    {
        await using var tx = await _context.Database.BeginTransactionAsync();

        var lease = await _context.Leases
            .Include(l => l.Tenant)
            .FirstOrDefaultAsync(l => l.LeaseId == id);
        if (lease == null || !CanAccessCompany(lease.Tenant?.CompanyId)) return NotFound();
        if (lease.Status != LeaseStatuses.Pending)
        {
            TempData["InfoMessage"] = $"Lease is {lease.Status}; only Pending leases can be Activated.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // Re-check occupancy at activation time: another lease may have been
        // created or activated on this unit since this one was drafted.
        var activateConflict = await LiveLeaseConflictAsync(lease.UnitId, lease.LeaseStartDate, lease.LeaseEndDate, excludeLeaseId: id);
        if (activateConflict != null)
        {
            TempData["ErrorMessage"] = "Cannot activate: " + DescribeConflict(activateConflict);
            return RedirectToAction(nameof(Details), new { id });
        }

        Invoice? depositInvoice = null;
        if (lease.SecurityDeposit > 0 && !lease.DepositInvoiced)
        {
            depositInvoice = new Invoice
            {
                LeaseId = lease.LeaseId,
                TenantId = lease.TenantId,
                InvoiceDate = lease.LeaseStartDate,
                DueDate = lease.LeaseStartDate, // due on (or before) move-in
                TotalAmount = lease.SecurityDeposit,
                PaidAmount = 0,
                Status = InvoiceStatuses.Pending,
                InvoiceType = InvoiceTypes.Deposit,
                Notes = $"Security deposit for lease #{lease.LeaseId}",
            };
            _context.Invoices.Add(depositInvoice);
            lease.DepositInvoiced = true;
        }

        lease.Status = LeaseStatuses.Active;
        lease.ModifiedDate = DateTime.UtcNow;

        // Sprint 13 — keep Unit.Status in sync with the lease lifecycle.
        // Skip Maintenance/Reserved so we don't override an operator hold.
        var unitToFlip = await _context.Units.FindAsync(lease.UnitId);
        if (unitToFlip != null
            && unitToFlip.Status != "Maintenance"
            && unitToFlip.Status != "Reserved")
        {
            unitToFlip.Status = "Occupied";
            unitToFlip.ModifiedDate = DateTime.UtcNow;
        }

        await _audit.WriteAsync(
            entityType: "Lease",
            entityId: lease.LeaseId,
            action: "Activated",
            byUser: User.Identity?.Name ?? "system",
            payload: new
            {
                fromStatus = LeaseStatuses.Pending,
                toStatus = LeaseStatuses.Active,
                depositInvoiceAmount = depositInvoice?.TotalAmount,
            });

        try
        {
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["ErrorMessage"] = "Lease was changed by someone else — refresh and retry.";
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["SuccessMessage"] = depositInvoice != null
            ? $"Lease activated. Deposit invoice #{depositInvoice.InvoiceId} for {depositInvoice.TotalAmount:0.000} KWD issued."
            : "Lease activated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var lease = await _context.Leases
            .Include(l => l.Tenant)
            .FirstOrDefaultAsync(l => l.LeaseId == id);
        if (lease == null || !CanAccessCompany(lease.Tenant?.CompanyId)) return NotFound();

        // LEASE_AUDIT Flaw #9 — closed leases are historical records. Edit
        // can still display them (operators want to view detail), but the
        // POST path will refuse to mutate. View uses ViewBag.IsClosed to
        // render fields read-only.
        ViewBag.IsClosed = IsClosed(lease.Status);
        // Sprint 11 — pre-select the Building so the cascade lands on the
        // correct unit list when the form first renders.
        var selectedPropertyId = await _context.Units
            .Where(u => u.UnitId == lease.UnitId)
            .Select(u => (int?)u.PropertyId)
            .FirstOrDefaultAsync();
        await PopulateCreateEditDropdownsAsync(
            lease.TenantId, lease.UnitId, selectedPropertyId, includeAllUnits: true);
        return View(lease);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("LeaseId,TenantId,LeaseStartDate,LeaseEndDate,MonthlyRent,SecurityDeposit,PaymentDueDay,LeaseType,LateFeeAmount,LateFeeGracePeriod,RowVersion,InsuranceAmount,InsuranceFrequency,InsuranceStartDate")] Lease lease)
    {
        if (id != lease.LeaseId) return NotFound();

        // LEASE_AUDIT Flaw #9 — re-read DB status so a maliciously-crafted
        // POST can't bypass the closed lock. Loaded TRACKED: we copy the
        // editable fields onto this row (load-then-copy) instead of
        // _context.Update(lease), which marked every unbound column — Unit,
        // renewal links, deposit flags, termination stamps, CreatedDate — as
        // modified and overwrote them with defaults.
        var existing = await _context.Leases
            .Include(l => l.Tenant)
            .FirstOrDefaultAsync(l => l.LeaseId == id);
        if (existing == null || !CanAccessCompany(existing.Tenant?.CompanyId)) return NotFound();
        if (IsClosed(existing.Status))
        {
            TempData["ErrorMessage"] =
                "Closed leases cannot be edited; create a renewal instead.";
            return RedirectToAction(nameof(Index));
        }

        // Status field isn't bound from the form anymore (Flaw #8); carry
        // the persisted value forward so the row keeps a valid state. UnitId
        // is no longer bound either — unit moves must go through Transfer so
        // both units' statuses stay in sync.
        lease.Status = existing.Status;
        lease.UnitId = existing.UnitId;

        ValidateLeaseFields(lease);

        // The tenant dropdown is still on the form; a change must point at a
        // tenant visible through the filtered Tenants set (company scope).
        if (lease.TenantId != existing.TenantId
            && !await _context.Tenants.AnyAsync(t => t.TenantId == lease.TenantId))
            ModelState.AddModelError(nameof(Lease.TenantId), "Select a valid tenant.");

        // Moving the dates must not make this lease overlap another live
        // lease on the same unit (e.g. extending into a pre-leased period).
        if (ModelState.IsValid && IsLive(existing.Status)
            && (lease.LeaseStartDate.Date != existing.LeaseStartDate.Date
                || lease.LeaseEndDate.Date != existing.LeaseEndDate.Date))
        {
            var editConflict = await LiveLeaseConflictAsync(existing.UnitId, lease.LeaseStartDate, lease.LeaseEndDate, excludeLeaseId: id);
            if (editConflict != null)
                ModelState.AddModelError(nameof(Lease.LeaseEndDate), DescribeConflict(editConflict));
        }

        if (ModelState.IsValid)
        {
            try
            {
                // Copy ONLY the fields the Edit form owns.
                existing.TenantId = lease.TenantId;
                existing.LeaseStartDate = lease.LeaseStartDate;
                existing.LeaseEndDate = lease.LeaseEndDate;
                existing.MonthlyRent = lease.MonthlyRent;
                existing.SecurityDeposit = lease.SecurityDeposit;
                existing.PaymentDueDay = lease.PaymentDueDay;
                existing.LeaseType = lease.LeaseType;
                existing.LateFeeAmount = lease.LateFeeAmount;
                existing.LateFeeGracePeriod = lease.LateFeeGracePeriod;
                existing.InsuranceAmount = lease.InsuranceAmount;
                existing.InsuranceFrequency = lease.InsuranceFrequency;
                existing.InsuranceStartDate = lease.InsuranceStartDate;
                existing.ModifiedDate = DateTime.UtcNow;

                // Optimistic concurrency: compare against the RowVersion the
                // form was rendered with, not the one we just re-read.
                if (lease.RowVersion != null)
                    _context.Entry(existing).Property(l => l.RowVersion).OriginalValue = lease.RowVersion;

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Lease updated successfully!";
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!LeaseExists(lease.LeaseId))
                {
                    return NotFound();
                }
                TempData["ErrorMessage"] = "Lease was changed by someone else — refresh and retry.";
                return RedirectToAction(nameof(Edit), new { id });
            }
            return RedirectToAction(nameof(Index));
        }

        // Sprint 11 — re-derive PropertyId from the lease's UnitId so the
        // cascade re-renders to the correct building when ModelState fails.
        var selectedPropertyId = await _context.Units
            .Where(u => u.UnitId == lease.UnitId)
            .Select(u => (int?)u.PropertyId)
            .FirstOrDefaultAsync();
        await PopulateCreateEditDropdownsAsync(
            lease.TenantId, lease.UnitId, selectedPropertyId, includeAllUnits: true);
        ViewBag.IsClosed = IsClosed(existing.Status);
        return View(lease);
    }

    /// <summary>
    /// Shared dropdown builder for Create + Edit (LEASE_AUDIT Flaw #15 —
    /// pick one tenant-visibility rule). Tenants show with their status
    /// suffix so operators know who's inactive; units show all when
    /// <paramref name="includeAllUnits"/> is true (Edit) and only Vacant
    /// when false (Create).
    /// </summary>
    private async Task PopulateCreateEditDropdownsAsync(
        int? selectedTenantId = null,
        int? selectedUnitId = null,
        int? selectedPropertyId = null,
        bool includeAllUnits = false)
    {
        // Tenants — alphabetical by first name. Inactive ones still listed so
        // an admin can resurrect a closed lease cycle if needed; the suffix
        // makes the state obvious in the dropdown. The live-lease count feeds
        // the "already holds N leases" hint on the Create form.
        var tenants = await _context.Tenants
            .OrderBy(t => t.FirstName)
            .Select(t => new
            {
                t.TenantId,
                FullName = t.FirstName + " " + t.LastName
                    + (t.Status != "Active" ? " (inactive)" : ""),
                t.PACINumber,
                t.Phone,
                LiveLeases = _context.Leases.Count(l => l.TenantId == t.TenantId
                    && (l.Status == LeaseStatuses.Active || l.Status == LeaseStatuses.Pending)),
            })
            .ToListAsync();
        ViewData["TenantId"] = new SelectList(tenants, "TenantId", "FullName", selectedTenantId);
        ViewData["TenantsJson"] = System.Text.Json.JsonSerializer.Serialize(
            tenants.ToDictionary(
                t => t.TenantId.ToString(),
                t => new { name = t.FullName, paci = t.PACINumber, phone = t.Phone, liveLeases = t.LiveLeases }));

        // Availability is LEASE-driven, not Unit.Status-driven: a unit is free
        // only when no Active/Pending lease claims it today. Unit.Status alone
        // let a Pending-leased unit show as Vacant and take a second lease.
        var today = DateTime.Today;
        var liveByUnit = (await _context.Leases
                .AsNoTracking()
                .Where(l => (l.Status == LeaseStatuses.Active || l.Status == LeaseStatuses.Pending)
                            && l.LeaseEndDate.Date >= today)
                .Select(l => new { l.UnitId, l.Status, l.LeaseEndDate, l.LeaseId,
                    Tenant = (l.Tenant.FirstName + " " + l.Tenant.LastName).Trim() })
                .ToListAsync())
            .GroupBy(l => l.UnitId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.LeaseEndDate).First());

        var allUnits = await _context.Units
            .Include(u => u.Property)
            .Where(u => _context.Properties.Any(p => p.PropertyId == u.PropertyId))
            .OrderBy(u => u.Property.PropertyName)
            .ThenBy(u => u.UnitNumber)
            .Select(u => new { u.UnitId, u.PropertyId, u.UnitNumber, u.UnitType, u.Status, u.MonthlyRent, u.Property.PropertyName })
            .ToListAsync();

        var unitRows = allUnits.Select(u =>
        {
            liveByUnit.TryGetValue(u.UnitId, out var live);
            var onHold = u.Status == "Maintenance" || u.Status == "Reserved";
            var available = live == null && !onHold;
            string? note = live != null
                ? $"{live.Status} lease #{live.LeaseId} · {live.Tenant} · until {live.LeaseEndDate:dd MMM yyyy}"
                : onHold ? u.Status : null;
            var suffix = live != null
                ? $" ({live.Status.ToLowerInvariant()} until {live.LeaseEndDate:dd MMM yyyy})"
                : onHold ? $" ({u.Status})" : "";
            return new
            {
                u.UnitId,
                u.PropertyId,
                u.PropertyName,
                DisplayName = $"{u.PropertyName} - Unit {u.UnitNumber}{suffix}",
                ShortName = $"Unit {u.UnitNumber}{suffix}",
                UnitNumber = u.UnitNumber,
                u.UnitType,
                u.MonthlyRent,
                Available = available,
                Note = note,
                AvailableFrom = live?.LeaseEndDate.AddDays(1),
            };
        }).ToList();

        // Buildings — active properties labelled with how many units can be
        // leased right now (lease-aware), so the operator goes straight to
        // the building with availability.
        var buildings = await _context.Properties
            .Where(p => p.Status == "Active")
            .OrderBy(p => p.PropertyName)
            .Select(p => new { p.PropertyId, p.PropertyName })
            .ToListAsync();
        var buildingItems = buildings
            .Select(b => new
            {
                b.PropertyId,
                Display = $"{b.PropertyName} ({unitRows.Count(u => u.PropertyId == b.PropertyId && u.Available)} available)",
            })
            .ToList();
        ViewData["PropertyId"] = new SelectList(buildingItems, "PropertyId", "Display", selectedPropertyId);

        // Create lists every unit (unavailable ones disabled, with the reason)
        // unless the caller asked for the old vacant-only behaviour. Edit
        // always lists all so the lease's own unit stays visible.
        var units = includeAllUnits ? unitRows : unitRows.Where(u => u.Available || u.UnitId == selectedUnitId).ToList();
        ViewData["UnitId"] = new SelectList(units, "UnitId", "DisplayName", selectedUnitId);

        // JSON dict keyed by PropertyId for the client-side cascade + summary.
        // Shape: { "1": [{id, text, unit, type, rent, available, note, availableFrom}], ... }
        var grouped = units
            .GroupBy(u => u.PropertyId)
            .ToDictionary(
                g => g.Key.ToString(),
                g => g.Select(u => new
                {
                    id = u.UnitId,
                    text = u.ShortName,
                    unit = u.UnitNumber,
                    type = u.UnitType,
                    rent = u.MonthlyRent,
                    available = u.Available,
                    note = u.Note,
                    availableFrom = u.AvailableFrom?.ToString("yyyy-MM-dd"),
                    property = u.PropertyName,
                }).ToList());
        ViewData["UnitsByBuildingJson"] = System.Text.Json.JsonSerializer.Serialize(grouped);
    }

    /// <summary>
    /// Server-side guardrails for Create + Edit POSTs (LEASE_AUDIT Flaw #12).
    /// HTML5 <c>type="date"</c> alone won't catch end-before-start or rents
    /// of zero, and certainly won't catch a hand-crafted POST.
    /// </summary>
    private void ValidateLeaseFields(Lease lease)
    {
        if (lease.LeaseEndDate <= lease.LeaseStartDate)
            ModelState.AddModelError(nameof(Lease.LeaseEndDate),
                "End date must be after start date.");
        if (lease.PaymentDueDay < 1 || lease.PaymentDueDay > 31)
            ModelState.AddModelError(nameof(Lease.PaymentDueDay),
                "Payment due day must be between 1 and 31.");
        if (lease.MonthlyRent <= 0)
            ModelState.AddModelError(nameof(Lease.MonthlyRent),
                "Monthly rent must be greater than zero.");
        if (lease.SecurityDeposit < 0)
            ModelState.AddModelError(nameof(Lease.SecurityDeposit),
                "Security deposit cannot be negative.");
    }

    [Authorize(Roles = Roles.AnyAdmin)]
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var lease = await _context.Leases
            .Include(l => l.Tenant)
            .Include(l => l.Unit)
                .ThenInclude(u => u.Property)
            .Include(l => l.Payments)
            .Include(l => l.Invoices)
            .FirstOrDefaultAsync(m => m.LeaseId == id);

        if (lease == null || !CanAccessCompany(lease.Tenant?.CompanyId))
        {
            return NotFound();
        }

        // LEASE_AUDIT Flaw #18 — surface the renewal-chain risk so the
        // operator knows deleting a Renewed lease orphans its successor's
        // back-link. Computed here so the view doesn't re-query.
        if (lease.RenewedToLeaseId.HasValue)
        {
            var successor = await _context.Leases
                .Where(l => l.LeaseId == lease.RenewedToLeaseId.Value)
                .Select(l => new { l.LeaseId, l.Status })
                .FirstOrDefaultAsync();
            ViewBag.SuccessorLeaseId = successor?.LeaseId;
            ViewBag.SuccessorIsActive = successor?.Status == LeaseStatuses.Active;
        }

        return View(lease);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.AnyAdmin)]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        // Company scope first so a cross-company id gets a plain 404 rather
        // than leaking "this lease has invoices".
        var lease = await _context.Leases
            .Include(l => l.Tenant)
            .Include(l => l.Unit)
            .FirstOrDefaultAsync(l => l.LeaseId == id);
        if (lease == null || !CanAccessCompany(lease.Tenant?.CompanyId)) return NotFound();

        // LEASE_AUDIT Flaw #19 — pre-check Invoices/Payments so the hard
        // delete doesn't throw a raw DbUpdateException at the FK Restrict.
        var hasInvoices = await _context.Invoices.AnyAsync(i => i.LeaseId == id);
        var hasPayments = await _context.Payments.AnyAsync(p => p.LeaseId == id);
        if (hasInvoices || hasPayments)
        {
            TempData["ErrorMessage"] =
                "This lease has invoices or payments and cannot be deleted. " +
                "Terminate it instead to preserve the financial history.";
            return RedirectToAction(nameof(Index));
        }

        // LegalCase.LeaseId is DeleteBehavior.Restrict too — a case filed on
        // this lease would make the delete 500. IgnoreQueryFilters: the FK
        // bites regardless of which company the case row belongs to.
        var hasLegalCases = await _context.LegalCases.IgnoreQueryFilters()
            .AnyAsync(c => c.LeaseId == id);
        if (hasLegalCases)
        {
            TempData["ErrorMessage"] =
                "This lease has a legal case filed against it and cannot be deleted. " +
                "Close or withdraw the case first, or terminate the lease instead.";
            return RedirectToAction(nameof(Index));
        }

        // Deleting an Active lease must free its unit, otherwise the unit
        // stays "Occupied" with no lease behind it until the nightly
        // reconcile job runs. Honour operator holds (Maintenance/Reserved).
        if (lease.Status == LeaseStatuses.Active
            && lease.Unit != null
            && lease.Unit.Status == "Occupied")
        {
            lease.Unit.Status = "Vacant";
            lease.Unit.ModifiedDate = DateTime.UtcNow;
        }

        _context.Leases.Remove(lease);
        await _audit.WriteAsync(
            entityType: "Lease",
            entityId: id,
            action: "Deleted",
            byUser: User.Identity?.Name ?? "system",
            payload: new { lease.UnitId, lease.TenantId, lease.Status });
        try
        {
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Lease deleted successfully!";
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Error deleting lease {LeaseId}", id);
            TempData["ErrorMessage"] =
                "This lease is referenced by other records and cannot be deleted. Terminate it instead.";
        }

        return RedirectToAction(nameof(Index));
    }

    private bool LeaseExists(int id)
    {
        return _context.Leases.Any(e => e.LeaseId == id);
    }

    /// <summary>
    /// Terminate a lease early. Now (post-LEASE_AUDIT) runs inside a single
    /// transaction that also flips the Unit to Vacant (#3), stamps the audit
    /// log (#16), and uses RowVersion for concurrency (#2). Idempotent
    /// at the status level.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.AnyAdmin)]
    public async Task<IActionResult> Terminate(int id, DateTime terminationDate, string? reason, decimal? depositRefundedAmount)
    {
        await using var tx = await _context.Database.BeginTransactionAsync();

        var lease = await _context.Leases
            .Include(l => l.Tenant)
            .Include(l => l.Unit)
            .FirstOrDefaultAsync(l => l.LeaseId == id);
        if (lease == null || !CanAccessCompany(lease.Tenant?.CompanyId)) return NotFound();
        if (IsClosed(lease.Status))
        {
            TempData["InfoMessage"] = "Lease is already closed.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // The form marks the date required, but a hand-crafted POST (or a
        // browser that doesn't honour type=date) binds default(DateTime) and
        // we'd stamp 0001-01-01 as the termination date.
        if (terminationDate == default || terminationDate == DateTime.MinValue)
        {
            TempData["ErrorMessage"] = "Termination date is required.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (depositRefundedAmount.HasValue && depositRefundedAmount.Value < 0)
        {
            TempData["ErrorMessage"] = "Deposit refund amount cannot be negative.";
            return RedirectToAction(nameof(Details), new { id });
        }
        // Never refund more than was ever held as deposit.
        if (depositRefundedAmount.HasValue && depositRefundedAmount.Value > lease.SecurityDeposit)
            depositRefundedAmount = lease.SecurityDeposit;

        lease.Status = LeaseStatuses.Terminated;
        lease.TerminationDate = terminationDate;
        lease.TerminationReason = reason;
        lease.TerminatedBy = User.Identity?.Name;
        // DepositRefundedAmount records the operator's figure; DepositRefunded
        // (the "money moved" flag) is only set below once a Refund row exists.
        if (depositRefundedAmount.HasValue)
        {
            lease.DepositRefundedAmount = depositRefundedAmount.Value;
        }
        lease.ModifiedDate = DateTime.UtcNow;

        // Deposit refund path — when an amount is provided, create a Refund
        // row tied to the most recent Completed deposit payment so the
        // existing RefundsController approval flow takes over (Pending →
        // Approved → Settled). Skipped silently when there's no matching
        // payment to tie to (e.g. deposit was never collected through the
        // system).
        Refund? refund = null;
        if (depositRefundedAmount.HasValue && depositRefundedAmount.Value > 0)
        {
            var depositPaymentId = await _context.Payments
                .Where(p => p.LeaseId == lease.LeaseId
                    && p.Status == PaymentStatus.Completed
                    && p.Invoice != null
                    && p.Invoice.InvoiceType == InvoiceTypes.Deposit)
                .OrderByDescending(p => p.PaymentDate)
                .Select(p => (int?)p.PaymentId)
                .FirstOrDefaultAsync();

            if (depositPaymentId.HasValue)
            {
                refund = new Refund
                {
                    PaymentId = depositPaymentId.Value,
                    Amount = depositRefundedAmount.Value,
                    Method = "BankTransfer",
                    Reason = $"Lease termination — deposit refund ({reason ?? "no reason"})",
                    Status = "Pending",
                    CreatedBy = User.Identity?.Name,
                };
                _context.Refunds.Add(refund);
                lease.DepositRefunded = true;
            }
        }

        // LEASE_AUDIT Flaw #3 — Terminate must free the Unit. Without this,
        // Properties/Snapshot occupancy stays wrong and the standard
        // LeasesController.Create vacancy filter hides the freed unit.
        if (lease.Unit != null)
        {
            lease.Unit.Status = "Vacant";
        }

        try
        {
            // First save materialises the new Refund.RefundId so the audit
            // payload can reference it. All writes still commit together
            // when the transaction commits below.
            await _context.SaveChangesAsync();

            await _audit.WriteAsync(
                entityType: "Lease",
                entityId: lease.LeaseId,
                action: "Terminated",
                byUser: User.Identity?.Name ?? "system",
                payload: new
                {
                    terminationDate, reason, depositRefundedAmount,
                    unitId = lease.UnitId,
                    refundId = refund?.RefundId,
                });
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["ErrorMessage"] = "Lease was changed by someone else — refresh and retry.";
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["SuccessMessage"] = refund != null
            ? $"Lease #{id} terminated. Refund #{refund.RefundId} created for {refund.Amount:0.000} KWD — pending approval."
            : $"Lease #{id} terminated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// Renew a lease. Now (post-LEASE_AUDIT) atomic: single transaction +
    /// single SaveChanges so the renewal insert and the old-lease flip
    /// commit together (#4). Concurrency-safe via RowVersion (#2). Accepts
    /// an optional newStartDate; default is max(oldEnd+1, today) so an
    /// Expired-lease renewal doesn't backdate (#17). Writes audit (#16).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.AnyAdmin)]
    public async Task<IActionResult> Renew(int id, DateTime newEndDate, decimal? newMonthlyRent, DateTime? newStartDate = null)
    {
        await using var tx = await _context.Database.BeginTransactionAsync();

        var lease = await _context.Leases
            .Include(l => l.Tenant)
            .FirstOrDefaultAsync(l => l.LeaseId == id);
        if (lease == null || !CanAccessCompany(lease.Tenant?.CompanyId)) return NotFound();
        if (lease.Status != LeaseStatuses.Active && lease.Status != LeaseStatuses.Expired)
        {
            TempData["ErrorMessage"] = "Only Active or Expired leases can be renewed.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (newMonthlyRent.HasValue && newMonthlyRent.Value <= 0)
        {
            TempData["ErrorMessage"] = "New monthly rent must be greater than zero.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (newEndDate == default)
        {
            TempData["ErrorMessage"] = "New end date is required.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (newEndDate <= lease.LeaseEndDate)
        {
            TempData["ErrorMessage"] = "New end date must be after the current end date.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // Default renewal start: pick whichever is later — day-after-old-end
        // OR today. This avoids backdating when the old lease was Expired
        // weeks ago. Operator can override by supplying newStartDate
        // explicitly; we reject only when it's before the old end.
        var defaultStart = lease.LeaseEndDate.AddDays(1);
        var renewalStart = newStartDate ?? (defaultStart < DateTime.Today ? DateTime.Today : defaultStart);
        if (renewalStart <= lease.LeaseEndDate)
        {
            TempData["ErrorMessage"] = "Renewal start must be after the current end date.";
            return RedirectToAction(nameof(Details), new { id });
        }
        // The unit may already be pre-leased to someone else for the renewal
        // period (the lease being renewed is excluded — it becomes Renewed).
        var renewConflict = await LiveLeaseConflictAsync(lease.UnitId, renewalStart, newEndDate, excludeLeaseId: id);
        if (renewConflict != null)
        {
            TempData["ErrorMessage"] = "Cannot renew: " + DescribeConflict(renewConflict);
            return RedirectToAction(nameof(Details), new { id });
        }
        if (newEndDate <= renewalStart)
        {
            TempData["ErrorMessage"] = "New end date must be after the renewal start.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var renewal = new Lease
        {
            UnitId = lease.UnitId,
            TenantId = lease.TenantId,
            LeaseStartDate = renewalStart,
            LeaseEndDate = newEndDate,
            MonthlyRent = newMonthlyRent ?? lease.MonthlyRent,
            SecurityDeposit = lease.SecurityDeposit,
            PaymentDueDay = lease.PaymentDueDay,
            LeaseType = lease.LeaseType,
            // New lease is queued as Pending until the old one is Renewed —
            // committed together below. This way the filtered unique
            // UQ_Leases_UnitId_Active can never see two Active rows for the
            // same unit, even during the transaction.
            Status = LeaseStatuses.Pending,
            LateFeeAmount = lease.LateFeeAmount,
            LateFeeGracePeriod = lease.LateFeeGracePeriod,
            // Carry the insurance plan forward — otherwise the renewal silently
            // drops to "None" and the insurance invoice job stops billing.
            InsuranceAmount = lease.InsuranceAmount,
            InsuranceFrequency = lease.InsuranceFrequency,
            InsuranceStartDate = lease.InsuranceStartDate,
            RenewedFromLeaseId = lease.LeaseId,
            CreatedDate = DateTime.UtcNow,
        };
        _context.Leases.Add(renewal);

        // Flip the old lease first so the unique-Active-per-unit index is
        // satisfied; then we'll promote the renewal to Active afterwards.
        lease.Status = LeaseStatuses.Renewed;
        lease.RenewedBy = User.Identity?.Name;
        lease.ModifiedDate = DateTime.UtcNow;

        try
        {
            await _context.SaveChangesAsync();
            // Now safe to promote the new lease and set the back-link.
            renewal.Status = LeaseStatuses.Active;
            lease.RenewedToLeaseId = renewal.LeaseId;
            await _audit.WriteAsync(
                entityType: "Lease",
                entityId: lease.LeaseId,
                action: "Renewed",
                byUser: User.Identity?.Name ?? "system",
                payload: new {
                    newLeaseId = renewal.LeaseId,
                    renewalStart, newEndDate,
                    monthlyRent = renewal.MonthlyRent
                });
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["ErrorMessage"] = "Lease was changed by someone else — refresh and retry.";
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["SuccessMessage"] = $"Lease #{id} renewed as #{renewal.LeaseId}.";
        return RedirectToAction(nameof(Details), new { id = renewal.LeaseId });
    }

    /// <summary>
    /// Transfer a tenant to a different unit. Now (post-LEASE_AUDIT) flips
    /// BOTH units' status — old → Vacant, new → Occupied (#11) — inside one
    /// transaction. Validates the target unit is actually Vacant. Writes
    /// audit + RowVersion concurrency check.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.AnyAdmin)]
    public async Task<IActionResult> Transfer(int id, int newUnitId, DateTime effectiveDate)
    {
        await using var tx = await _context.Database.BeginTransactionAsync();

        var lease = await _context.Leases
            .Include(l => l.Tenant)
            .Include(l => l.Unit)
            .FirstOrDefaultAsync(l => l.LeaseId == id);
        if (lease == null || !CanAccessCompany(lease.Tenant?.CompanyId)) return NotFound();
        if (lease.Status != LeaseStatuses.Active)
        {
            TempData["ErrorMessage"] = "Only Active leases can be transferred.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (effectiveDate == default || effectiveDate == DateTime.MinValue)
        {
            TempData["ErrorMessage"] = "Effective date is required.";
            return RedirectToAction(nameof(Details), new { id });
        }
        // Target unit must belong to the caller's company too — Unit has no
        // company filter, but its Property does, so check through Properties.
        var newUnit = await _context.Units.FindAsync(newUnitId);
        if (newUnit == null
            || !await _context.Properties.AnyAsync(p => p.PropertyId == newUnit.PropertyId))
        {
            TempData["ErrorMessage"] = "Target unit not found.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (newUnit.Status != "Vacant")
        {
            TempData["ErrorMessage"] = "Target unit is not vacant.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (newUnitId == lease.UnitId)
        {
            TempData["ErrorMessage"] = "Cannot transfer to the same unit.";
            return RedirectToAction(nameof(Details), new { id });
        }
        // "Vacant" is the unit flag; the leases table is the truth. A unit can
        // be flagged Vacant and still hold a Pending lease for the same dates.
        var transferConflict = await LiveLeaseConflictAsync(newUnitId, effectiveDate, lease.LeaseEndDate);
        if (transferConflict != null)
        {
            TempData["ErrorMessage"] = "Cannot transfer: " + DescribeConflict(transferConflict);
            return RedirectToAction(nameof(Details), new { id });
        }

        var newLease = new Lease
        {
            UnitId = newUnitId,
            TenantId = lease.TenantId,
            LeaseStartDate = effectiveDate,
            LeaseEndDate = lease.LeaseEndDate,
            MonthlyRent = lease.MonthlyRent,
            SecurityDeposit = lease.SecurityDeposit,
            PaymentDueDay = lease.PaymentDueDay,
            LeaseType = lease.LeaseType,
            // Stage as Pending — promoted to Active after old lease is Terminated.
            // Keeps the filtered unique index satisfied throughout the TX.
            Status = LeaseStatuses.Pending,
            LateFeeAmount = lease.LateFeeAmount,
            LateFeeGracePeriod = lease.LateFeeGracePeriod,
            // Carry the insurance plan across the move (same tenant, same cover).
            InsuranceAmount = lease.InsuranceAmount,
            InsuranceFrequency = lease.InsuranceFrequency,
            InsuranceStartDate = lease.InsuranceStartDate,
            // Link back to the predecessor so /Leases/History walks the chain
            // through the transfer (Lease has no Notes column to hold a link).
            RenewedFromLeaseId = lease.LeaseId,
            CreatedDate = DateTime.UtcNow,
        };
        _context.Leases.Add(newLease);

        // Close old lease + free old unit.
        lease.Status = LeaseStatuses.Terminated;
        lease.TerminationDate = effectiveDate;
        lease.TerminationReason = $"Transfer to unit {newUnitId}";
        lease.TerminatedBy = User.Identity?.Name;
        lease.TransferredBy = User.Identity?.Name;
        lease.ModifiedDate = DateTime.UtcNow;
        if (lease.Unit != null) lease.Unit.Status = "Vacant";

        try
        {
            await _context.SaveChangesAsync();
            // Promote the new lease and occupy the new unit.
            newLease.Status = LeaseStatuses.Active;
            newUnit.Status = "Occupied";
            lease.RenewedToLeaseId = newLease.LeaseId;
            lease.TerminationReason = $"Transfer to unit {newUnitId} (new lease #{newLease.LeaseId})";
            await _audit.WriteAsync(
                entityType: "Lease",
                entityId: lease.LeaseId,
                action: "Transferred",
                byUser: User.Identity?.Name ?? "system",
                payload: new {
                    newLeaseId = newLease.LeaseId,
                    fromUnitId = lease.UnitId, toUnitId = newUnitId,
                    effectiveDate
                });
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["ErrorMessage"] = "Lease was changed by someone else — refresh and retry.";
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["SuccessMessage"] = $"Lease #{id} transferred to new lease #{newLease.LeaseId}.";
        return RedirectToAction(nameof(Details), new { id = newLease.LeaseId });
    }

    /// <summary>
    /// Sprint 20 — save inline-uploaded documents and link them to the new lease.
    /// Mirrors the TenantsController/PropertiesController shape so the on-disk
    /// layout (<c>wwwroot/uploads/documents/</c>) is the same regardless of
    /// which form the file came in through. The /Documents Index already
    /// picks these up because the listing reads from Documents+DocumentLinks.
    /// </summary>
    private async Task UploadDocuments(
        int entityId, List<IFormFile> documents, List<string> documentTypes, string entityType)
    {
        try
        {
            var uploadsPath = Path.Combine(_environment.WebRootPath, "uploads", "documents");
            if (!Directory.Exists(uploadsPath))
                Directory.CreateDirectory(uploadsPath);

            for (int i = 0; i < documents.Count; i++)
            {
                var file = documents[i];
                if (file == null || file.Length <= 0
                    || i >= documentTypes.Count
                    || string.IsNullOrEmpty(documentTypes[i])
                    || !UploadValidation.IsAllowed(file))
                    continue;

                var fileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
                var filePath = Path.Combine(uploadsPath, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                    await file.CopyToAsync(stream);

                _context.Documents.Add(new Document
                {
                    DocumentName = file.FileName,
                    DocumentType = documentTypes[i],
                    FilePath = Path.Combine("uploads", "documents", fileName),
                    FileSize = file.Length,
                    RelatedEntityType = entityType,
                    RelatedEntityId = entityId,
                    Description = $"{documentTypes[i]} for {entityType}",
                    UploadedBy = User.Identity?.Name ?? "system",
                    UploadedDate = DateTime.UtcNow,
                });
            }

            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading documents for {EntityType} {EntityId}", entityType, entityId);
        }
    }
}
