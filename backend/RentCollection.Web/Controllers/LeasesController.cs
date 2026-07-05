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

    #region Leases landing — Sprint 1 endpoints

    /// <summary>
    /// Portfolio KPI strip for /Leases. Mirrors Tenants/Snapshot shape so
    /// the view can reuse the same skeleton-shimmer pattern. Cached for
    /// 30 s — list page reloads cheap.
    /// </summary>
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Snapshot()
    {
        var today = DateTime.Today;
        var soon = today.AddDays(30);

        var totalLeases = await _context.Leases.CountAsync();
        var activeLeases = await _context.Leases.CountAsync(l => l.Status == LeaseStatuses.Active);

        // Expiring soon = Active AND end ≤ today+30 AND end ≥ today.
        var expiringSoon = await _context.Leases.CountAsync(l =>
            l.Status == LeaseStatuses.Active &&
            l.LeaseEndDate >= today &&
            l.LeaseEndDate <= soon);

        // Expired-but-still-Active = lease past end date but never closed.
        // These usually need the operator to renew or terminate.
        var expired = await _context.Leases.CountAsync(l =>
            l.Status == LeaseStatuses.Active && l.LeaseEndDate < today);

        // Monthly rent roll from Active leases.
        var monthlyRentRoll = await _context.Leases
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
        var q = _context.Leases
            .Include(l => l.Tenant)
            .Include(l => l.Unit).ThenInclude(u => u.Property)
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
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Any, VaryByQueryKeys = new[] { "ids" })]
    public async Task<IActionResult> KpiBatch(string? ids)
    {
        var idList = (ids ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .Where(n => n > 0).Distinct().ToList();
        if (idList.Count == 0) return Json(Array.Empty<object>());

        var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        // Outstanding = sum of (TotalAmount − PaidAmount) over unpaid invoices
        // grouped by lease. Matches the same definition used on /Invoices.
        var outstandingRaw = await _context.Invoices
            .Where(i => idList.Contains(i.LeaseId)
                        && i.Status != "Paid" && i.TotalAmount - i.PaidAmount > 0)
            .GroupBy(i => i.LeaseId)
            .Select(g => new { LeaseId = g.Key, Outstanding = g.Sum(i => i.TotalAmount - i.PaidAmount) })
            .ToListAsync();

        // Month-to-date collected = sum of completed payments whose date is
        // in the current calendar month, grouped by lease.
        var mtdRaw = await _context.Payments
            .Where(p => idList.Contains(p.LeaseId)
                        && p.Status == PaymentStatus.Completed
                        && p.PaymentDate >= monthStart)
            .GroupBy(p => p.LeaseId)
            .Select(g => new { LeaseId = g.Key, Mtd = g.Sum(p => p.AmountPaid) })
            .ToListAsync();

        // Last payment date over the lifetime of the lease — drives the
        // "Last paid" sub-label and the per-row health colour.
        var lastRaw = await _context.Payments
            .Where(p => idList.Contains(p.LeaseId)
                        && p.Status == PaymentStatus.Completed)
            .GroupBy(p => p.LeaseId)
            .Select(g => new { LeaseId = g.Key, LastDate = g.Max(p => p.PaymentDate) })
            .ToListAsync();

        var outstandingMap = outstandingRaw.ToDictionary(x => x.LeaseId, x => x.Outstanding);
        var mtdMap = mtdRaw.ToDictionary(x => x.LeaseId, x => x.Mtd);
        var lastMap = lastRaw.ToDictionary(x => x.LeaseId, x => x.LastDate);

        var rows = idList.Select(id => new
        {
            leaseId = id,
            outstanding = outstandingMap.GetValueOrDefault(id, 0m),
            mtdCollected = mtdMap.GetValueOrDefault(id, 0m),
            lastPaymentDate = lastMap.TryGetValue(id, out var d) ? d : (DateTime?)null,
        });
        return Json(rows);
    }

    #endregion

    public async Task<IActionResult> Index()
    {
        try
        {
            var leases = await _context.Leases
                .Include(l => l.Tenant)
                .Include(l => l.Unit)
                    .ThenInclude(u => u.Property)
                .OrderByDescending(l => l.CreatedDate)
                .ToListAsync();

            return View(leases);
        }
        catch (Exception)
        {
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

        if (lease == null)
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
        if (lease == null) return NotFound();

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
                    LeaseEndDate = DateTime.Today.AddYears(1), // sensible default — 1 year
                    PaymentDueDay = 1,                         // most common
                    LeaseType = "Fixed",
                    InsuranceFrequency = "None",
                };

                // Soft warning when the deep link targets a non-Vacant unit
                // (the operator may have meant a renewal, transfer, or hold).
                if (!string.Equals(unit.Status, "Vacant", StringComparison.OrdinalIgnoreCase))
                    TempData["InfoMessage"] =
                        $"Unit is currently {unit.Status}. Creating a new lease here will mark it Occupied; for a renewal use the existing lease's Renew action instead.";
            }
        }

        // includeAllUnits when prefilling so the linked unit still appears even
        // if it isn't Vacant (e.g. the operator is reviewing a renewal flow on
        // an Occupied unit). The Unit dropdown displays "(Status)" inline so
        // they can tell at a glance which units are available.
        await PopulateCreateEditDropdownsAsync(
            selectedUnitId: prefill?.UnitId,
            selectedPropertyId: selectedPropertyId,
            includeAllUnits: prefill != null);
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

            await _context.SaveChangesAsync();

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

        var lease = await _context.Leases.FirstOrDefaultAsync(l => l.LeaseId == id);
        if (lease == null) return NotFound();
        if (lease.Status != LeaseStatuses.Pending)
        {
            TempData["InfoMessage"] = $"Lease is {lease.Status}; only Pending leases can be Activated.";
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

        var lease = await _context.Leases.FindAsync(id);
        if (lease == null) return NotFound();

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
    public async Task<IActionResult> Edit(int id, [Bind("LeaseId,TenantId,UnitId,LeaseStartDate,LeaseEndDate,MonthlyRent,SecurityDeposit,PaymentDueDay,LeaseType,LateFeeAmount,LateFeeGracePeriod,CreatedDate,RowVersion,InsuranceAmount,InsuranceFrequency,InsuranceStartDate")] Lease lease)
    {
        if (id != lease.LeaseId) return NotFound();

        // LEASE_AUDIT Flaw #9 — re-read DB status so a maliciously-crafted
        // POST can't bypass the closed lock.
        var existing = await _context.Leases.AsNoTracking().FirstOrDefaultAsync(l => l.LeaseId == id);
        if (existing == null) return NotFound();
        if (IsClosed(existing.Status))
        {
            TempData["ErrorMessage"] =
                "Closed leases cannot be edited; create a renewal instead.";
            return RedirectToAction(nameof(Index));
        }

        // Status field isn't bound from the form anymore (Flaw #8); carry
        // the persisted value forward so the row keeps a valid state.
        lease.Status = existing.Status;

        ValidateLeaseFields(lease);

        if (ModelState.IsValid)
        {
            try
            {
                lease.ModifiedDate = DateTime.UtcNow;
                _context.Update(lease);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Lease updated successfully!";
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!LeaseExists(lease.LeaseId))
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

        // Sprint 11 — re-derive PropertyId from the submitted UnitId so the
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
        // makes the state obvious in the dropdown.
        var tenants = await _context.Tenants
            .OrderBy(t => t.FirstName)
            .Select(t => new
            {
                t.TenantId,
                FullName = t.FirstName + " " + t.LastName
                    + (t.Status != "Active" ? " (inactive)" : ""),
            })
            .ToListAsync();
        ViewData["TenantId"] = new SelectList(tenants, "TenantId", "FullName", selectedTenantId);

        // Buildings — all active properties, labelled with their vacant-unit
        // count so the operator picks the building with availability without
        // having to first drill into Units.
        var buildings = await _context.Properties
            .Where(p => p.Status == "Active")
            .Select(p => new
            {
                p.PropertyId,
                p.PropertyName,
                VacantCount = _context.Units.Count(u => u.PropertyId == p.PropertyId && u.Status == "Vacant"),
            })
            .OrderBy(p => p.PropertyName)
            .ToListAsync();
        var buildingItems = buildings
            .Select(b => new
            {
                b.PropertyId,
                Display = $"{b.PropertyName} ({b.VacantCount} vacant)",
            })
            .ToList();
        ViewData["PropertyId"] = new SelectList(buildingItems, "PropertyId", "Display", selectedPropertyId);

        // Units — same filter as before (Vacant only on Create, all on Edit so
        // the existing lease's unit stays visible). Plus we also dump the same
        // list grouped by PropertyId as JSON for the client-side filter.
        var unitsQ = _context.Units.Include(u => u.Property).AsQueryable();
        if (!includeAllUnits) unitsQ = unitsQ.Where(u => u.Status == "Vacant");
        var units = await unitsQ
            .OrderBy(u => u.Property.PropertyName)
            .ThenBy(u => u.UnitNumber)
            .Select(u => new
            {
                u.UnitId,
                u.PropertyId,
                DisplayName = u.Property.PropertyName + " - Unit " + u.UnitNumber
                    + (u.Status != "Vacant" ? " (" + u.Status + ")" : ""),
                ShortName = "Unit " + u.UnitNumber
                    + (u.Status != "Vacant" ? " (" + u.Status + ")" : ""),
            })
            .ToListAsync();
        // Keep the legacy SelectList so model binding on POST still works when
        // ModelState is invalid and we re-render. The cascade JS rebuilds the
        // options client-side anyway.
        ViewData["UnitId"] = new SelectList(units, "UnitId", "DisplayName", selectedUnitId);

        // JSON dict keyed by PropertyId for the client-side cascade.
        // Shape: { "1": [{id:7, text:"Unit 102 (Vacant)"}, ...], "2": [...] }
        var grouped = units
            .GroupBy(u => u.PropertyId)
            .ToDictionary(
                g => g.Key.ToString(),
                g => g.Select(u => new { id = u.UnitId, text = u.ShortName }).ToList());
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

        if (lease == null)
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

        var lease = await _context.Leases.FindAsync(id);
        if (lease != null)
        {
            _context.Leases.Remove(lease);
            await _audit.WriteAsync(
                entityType: "Lease",
                entityId: id,
                action: "Deleted",
                byUser: User.Identity?.Name ?? "system",
                payload: new { lease.UnitId, lease.TenantId, lease.Status });
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Lease deleted successfully!";
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

        var lease = await _context.Leases.Include(l => l.Unit).FirstOrDefaultAsync(l => l.LeaseId == id);
        if (lease == null) return NotFound();
        if (IsClosed(lease.Status))
        {
            TempData["InfoMessage"] = "Lease is already closed.";
            return RedirectToAction(nameof(Details), new { id });
        }

        lease.Status = LeaseStatuses.Terminated;
        lease.TerminationDate = terminationDate;
        lease.TerminationReason = reason;
        lease.TerminatedBy = User.Identity?.Name;
        if (depositRefundedAmount.HasValue)
        {
            lease.DepositRefunded = depositRefundedAmount.Value > 0;
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

        var lease = await _context.Leases.FindAsync(id);
        if (lease == null) return NotFound();
        if (lease.Status != LeaseStatuses.Active && lease.Status != LeaseStatuses.Expired)
        {
            TempData["ErrorMessage"] = "Only Active or Expired leases can be renewed.";
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

        var lease = await _context.Leases.Include(l => l.Unit).FirstOrDefaultAsync(l => l.LeaseId == id);
        if (lease == null) return NotFound();
        if (lease.Status != LeaseStatuses.Active)
        {
            TempData["ErrorMessage"] = "Only Active leases can be transferred.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var newUnit = await _context.Units.FindAsync(newUnitId);
        if (newUnit == null)
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
                    || string.IsNullOrEmpty(documentTypes[i]))
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
