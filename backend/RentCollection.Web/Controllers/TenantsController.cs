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
public class TenantsController : Controller
{
    /// <summary>One live lease line for the tenants list (2026-10 redesign).</summary>
    public sealed record TenantLeaseRow(int TenantId, int LeaseId, string PropertyName, string UnitNumber,
        DateTime LeaseEndDate, decimal MonthlyRent, string Status);

    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly IPdfService _pdf;
    private readonly ITenantCredentialService _credentials;
    private readonly ILogger<TenantsController> _logger;

    public TenantsController(
        ApplicationDbContext context,
        IWebHostEnvironment environment,
        IPdfService pdf,
        ITenantCredentialService credentials,
        ILogger<TenantsController> logger)
    {
        _context = context;
        _environment = environment;
        _pdf = pdf;
        _credentials = credentials;
        _logger = logger;
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    public async Task<IActionResult> Index(bool includeArchived = false)
    {
        try
        {
            // includeArchived=true bypasses the global ISoftDeletable filter so
            // the toolbar "Show archived" toggle can show soft-deleted rows
            // alongside live ones. They render with .is-archived styling and
            // expose a Restore action in the kebab (Sprint 4).
            // SECURITY: IgnoreQueryFilters() also strips the company scope, so
            // re-apply it by hand — archived rows must still be our own.
            var isSuperAdmin = User.IsInRole(Roles.SuperAdmin);
            var cid = ScopedCompanyId() ?? -1;
            var q = includeArchived
                ? _context.Tenants.IgnoreQueryFilters().Where(t => isSuperAdmin || t.CompanyId == cid)
                : _context.Tenants.AsQueryable();

            var tenants = await q
                .AsNoTracking()
                .OrderByDescending(t => t.CreatedDate)
                .ToListAsync();

            // Load properties separately to match via PACI (read-only → no tracking)
            var properties = await _context.Properties.AsNoTracking().ToListAsync();

            ViewBag.Properties = properties;
            ViewBag.IncludeArchived = includeArchived;

            // 2026-10 list redesign — where each tenant lives right now (live
            // leases → property · unit, earliest end, total rent), so the list
            // shows the facts an operator actually scans for, in one line.
            var tenantIds = tenants.Select(t => t.TenantId).ToList();
            var liveLeases = await _context.Leases.AsNoTracking()
                .Where(l => tenantIds.Contains(l.TenantId)
                            && (l.Status == LeaseStatuses.Active || l.Status == LeaseStatuses.Pending))
                .Select(l => new TenantLeaseRow(
                    l.TenantId, l.LeaseId, l.Unit.Property.PropertyName, l.Unit.UnitNumber,
                    l.LeaseEndDate, l.MonthlyRent, l.Status))
                .ToListAsync();
            ViewBag.LeasesByTenant = liveLeases
                .GroupBy(l => l.TenantId)
                .ToDictionary(g => g.Key, g => g.OrderBy(l => l.LeaseEndDate).ToList());

            // Latest legal case per tenant → drives the ⚖️ badge in the list.
            var cases = await _context.LegalCases.AsNoTracking()
                .OrderByDescending(c => c.FiledDate).ThenByDescending(c => c.LegalCaseId)
                .ToListAsync();
            ViewBag.CaseByTenant = cases
                .GroupBy(c => c.TenantId)
                .ToDictionary(g => g.Key, g => g.First());

            return View(tenants);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading tenants list");
            ViewBag.ErrorMessage = "An error occurred while loading tenants. Please try again.";
            return View(new List<Tenant>());
        }
    }

    #region Tenants landing — Sprints 1/2/3 endpoints

    /// <summary>
    /// Portfolio KPI strip for the /Tenants landing. One round trip
    /// (5 aggregates) — same pattern as Properties/Snapshot.
    /// </summary>
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> Snapshot()
    {
        var today = DateTime.Today;
        var totalTenants = await _context.Tenants.CountAsync();
        var activeTenants = await _context.Tenants.CountAsync(t => t.Status == "Active");

        // "With active leases" — distinct tenants who have at least one Active lease.
        var withActiveLeases = await _context.Leases
            .Where(l => l.Status == LeaseStatuses.Active)
            .Select(l => l.TenantId)
            .Distinct()
            .CountAsync();

        // "In arrears" — distinct tenants whose unpaid invoice balances sum to > 0.
        var arrearsTenants = await _context.Invoices
            .Where(i => i.Status != "Paid" && i.TotalAmount - i.PaidAmount > 0)
            .Select(i => i.TenantId)
            .Distinct()
            .CountAsync();

        // Average tenure in months (CreatedDate → today). NULL-safe for empty list.
        var avgTenureMonths = totalTenants == 0
            ? 0
            : await _context.Tenants
                .Select(t => EF.Functions.DateDiffMonth(t.CreatedDate, today))
                .AverageAsync(d => (double)d);

        return Json(new
        {
            generatedAt = DateTime.UtcNow,
            totalTenants,
            activeTenants,
            withActiveLeases,
            arrearsTenants,
            avgTenureMonths = Math.Round(avgTenureMonths, 1),
        });
    }

    /// <summary>
    /// Per-tenant KPI batch — Outstanding, Last Payment, Active Leases, Health.
    /// One round trip for all visible rows. Mirrors Properties/KpiBatch shape.
    /// Health thresholds configurable via Tenants:OverdueAmberDays /
    /// Tenants:OverdueRedDays (defaults 30 / 30).
    /// </summary>
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Client, VaryByQueryKeys = new[] { "ids" })]
    public async Task<IActionResult> KpiBatch(string? ids, [FromServices] IConfiguration config)
    {
        var idList = (ids ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .Where(n => n > 0).Distinct().ToList();

        var today = DateTime.Today;
        var amberDays = config.GetValue<int?>("Tenants:OverdueAmberDays") ?? 30;
        var redDays = config.GetValue<int?>("Tenants:OverdueRedDays") ?? 30;

        var invoicesQuery = _context.Invoices.AsQueryable();
        var paymentsQuery = _context.Payments.Where(p => p.Status == PaymentStatus.Completed);
        var leasesQuery = _context.Leases.Where(l => l.Status == LeaseStatuses.Active);
        // IgnoreQueryFilters() so archived rows asked for via includeArchived still
        // get their KPIs populated — otherwise the row stays stuck on skeletons.
        var tenantArchivedQuery = _context.Tenants.IgnoreQueryFilters();
        if (idList.Count > 0)
        {
            invoicesQuery = invoicesQuery.Where(i => idList.Contains(i.TenantId));
            paymentsQuery = paymentsQuery.Where(p => idList.Contains(p.TenantId));
            leasesQuery = leasesQuery.Where(l => idList.Contains(l.TenantId));
            tenantArchivedQuery = tenantArchivedQuery.Where(t => idList.Contains(t.TenantId));
        }

        // Outstanding balance + oldest unpaid invoice date — drives Health.
        var unpaidRaw = await invoicesQuery
            .Where(i => i.Status != "Paid" && i.TotalAmount - i.PaidAmount > 0)
            .GroupBy(i => i.TenantId)
            .Select(g => new
            {
                TenantId = g.Key,
                Outstanding = g.Sum(i => i.TotalAmount - i.PaidAmount),
                OldestDue = g.Min(i => (DateTime?)i.DueDate),
            })
            .ToListAsync();

        var lastPaymentRaw = await paymentsQuery
            .GroupBy(p => p.TenantId)
            .Select(g => new { TenantId = g.Key, LastDate = g.Max(p => p.PaymentDate) })
            .ToListAsync();

        var activeLeasesRaw = await leasesQuery
            .GroupBy(l => l.TenantId)
            .Select(g => new
            {
                TenantId = g.Key,
                Count = g.Count(),
                // Earliest active-lease end date — drives the "Lease expiring ≤ N days"
                // toolbar filter so the chip actually narrows the visible rows.
                EarliestEnd = g.Min(l => (DateTime?)l.LeaseEndDate),
            })
            .ToListAsync();

        // Soft-delete flag — surfaced to the row so the view can paint .is-archived
        // and swap the kebab menu for Restore when "Show archived" is on.
        var archivedRaw = await tenantArchivedQuery
            .Where(t => t.IsDeleted)
            .Select(t => t.TenantId)
            .ToListAsync();

        var unpaidMap = unpaidRaw.ToDictionary(x => x.TenantId, x => x);
        var lastMap = lastPaymentRaw.ToDictionary(x => x.TenantId, x => x.LastDate);
        var leaseMap = activeLeasesRaw.ToDictionary(x => x.TenantId, x => x);
        var archivedSet = new HashSet<int>(archivedRaw);

        string HealthFor(decimal outstanding, DateTime? oldestDue)
        {
            if (outstanding <= 0) return "good";
            if (oldestDue == null) return "amber";
            var daysOverdue = (today - oldestDue.Value.Date).Days;
            if (daysOverdue > redDays) return "bad";
            if (daysOverdue > amberDays) return "bad"; // amber+red both = bad past threshold
            return "amber";
        }

        var rows = idList.Select(id =>
        {
            var u = unpaidMap.TryGetValue(id, out var x) ? x : null;
            var leaseInfo = leaseMap.TryGetValue(id, out var li) ? li : null;
            return new
            {
                tenantId = id,
                outstanding = u?.Outstanding ?? 0m,
                oldestDueDate = u?.OldestDue,
                lastPaymentDate = lastMap.TryGetValue(id, out var d) ? d : (DateTime?)null,
                activeLeases = leaseInfo?.Count ?? 0,
                leaseEndDate = leaseInfo?.EarliestEnd,
                archived = archivedSet.Contains(id),
                health = HealthFor(u?.Outstanding ?? 0m, u?.OldestDue),
            };
        });
        return Json(rows);
    }

    /// <summary>
    /// Sprint 6 — CSV/XLSX import. GET renders the upload form (the user
    /// can download a blank template). POST parses the uploaded file,
    /// validates each row, inserts what passes, and renders ImportResult
    /// with per-row success/skip reasons. Round-trips cleanly with
    /// ExportExcel because the column headers match exactly.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = Roles.AnyAdmin)]
    public IActionResult Import() => View();

    /// <summary>
    /// Downloads a blank XLSX template with the same header row as
    /// ExportExcel so users can edit the exported file and re-import it.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = Roles.AnyAdmin)]
    public IActionResult ImportTemplate()
    {
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("Tenants");
        var headers = new[] { "First Name", "Last Name", "Email", "Phone", "Client PACI", "Building PACI", "Government ID", "Status", "Company" };
        for (int c = 0; c < headers.Length; c++)
        {
            ws.Cells[1, c + 1].Value = headers[c];
            ws.Cells[1, c + 1].Style.Font.Bold = true;
            ws.Cells[1, c + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[1, c + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(99, 102, 241));
            ws.Cells[1, c + 1].Style.Font.Color.SetColor(System.Drawing.Color.White);
        }
        // Example row to make the format unambiguous; users delete it.
        ws.Cells[2, 1].Value = "John";
        ws.Cells[2, 2].Value = "Doe";
        ws.Cells[2, 3].Value = "john.doe@example.com";
        ws.Cells[2, 4].Value = "12345678";
        ws.Cells[2, 5].Value = "123456789012";
        ws.Cells[2, 8].Value = "Active";
        ws.Cells[2, 9].Value = "Acme Holdings";
        ws.Cells[ws.Dimension.Address].AutoFitColumns();
        return File(pkg.GetAsByteArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "tenants-import-template.xlsx");
    }

    public class ImportResultRow
    {
        public int Row { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool Imported { get; set; }
        public string? Reason { get; set; }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.AnyAdmin)]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB cap
    public async Task<IActionResult> Import(IFormFile? file)
    {
        var results = new List<ImportResultRow>();
        if (file == null || file.Length == 0)
        {
            ViewBag.ErrorMessage = "Pick a .csv or .xlsx file first.";
            return View();
        }

        // Pull every row into a uniform List<string[]> first so .csv and .xlsx
        // share the same validate-and-insert path below.
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        List<string?[]> rows;
        try
        {
            await using var s = file.OpenReadStream();
            if (ext == ".csv")
                rows = ReadCsv(s);
            else if (ext == ".xlsx")
                rows = ReadXlsx(s);
            else
            {
                ViewBag.ErrorMessage = $"Unsupported file type '{ext}'. Use .csv or .xlsx.";
                return View();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse import file {Name}", file.FileName);
            ViewBag.ErrorMessage = "Couldn't parse the file. Make sure it is a valid .csv or .xlsx and try again.";
            return View();
        }

        if (rows.Count < 2)
        {
            ViewBag.ErrorMessage = "File looks empty — needs a header row plus at least one data row.";
            return View();
        }

        // Map the header row to column indices so users can re-order columns
        // or drop optional ones (Company, Government ID, etc.) without breaking.
        var headers = (rows[0] ?? Array.Empty<string?>()).Select(h => (h ?? "").Trim().ToLowerInvariant()).ToList();
        int Col(params string[] candidates) =>
            candidates.Select(c => headers.IndexOf(c.ToLowerInvariant())).FirstOrDefault(i => i >= 0);
        int iFirst   = Col("first name", "firstname");
        int iLast    = Col("last name", "lastname");
        int iEmail   = Col("email");
        int iPhone   = Col("phone");
        int iPaci    = Col("client paci", "paci", "paci number");
        int iBldgPaci = Col("building paci", "building paci number");
        int iGov     = Col("government id", "governmentid");
        int iStatus  = Col("status");
        int iCompany = Col("company", "company name");

        if (iFirst < 0 && iLast < 0)
        {
            ViewBag.ErrorMessage = "Couldn't find First Name / Last Name columns in the header row.";
            return View();
        }

        // Pre-load existing dedupe keys (Email, PACINumber) and Company lookup
        // — one round-trip each, so the per-row loop stays in memory.
        // SECURITY: dedupe within the caller's company only. IgnoreQueryFilters()
        // keeps archived rows in the set but we re-pin CompanyId by hand so a
        // company-A import never probes company-B's emails / Civil IDs.
        var isSuperAdmin = User.IsInRole(Roles.SuperAdmin);
        var callerCompanyId = ScopedCompanyId() ?? -1;
        var dedupeScope = _context.Tenants.IgnoreQueryFilters()
            .Where(t => isSuperAdmin || t.CompanyId == callerCompanyId);
        var existingEmails = new HashSet<string>(
            await dedupeScope
                .Where(t => t.Email != null && t.Email != "")
                .Select(t => t.Email!.ToLower()).ToListAsync(),
            StringComparer.OrdinalIgnoreCase);
        var existingPacis = new HashSet<string>(
            await dedupeScope
                .Where(t => t.PACINumber != null && t.PACINumber != "")
                .Select(t => t.PACINumber!).ToListAsync(),
            StringComparer.OrdinalIgnoreCase);
        var companies = await _context.Companies
            .Select(c => new { c.CompanyId, c.CompanyName })
            .ToListAsync();
        var companyByName = companies.ToDictionary(c => c.CompanyName.ToLowerInvariant(), c => c.CompanyId);
        // Non-SuperAdmins always import into their own company (the Company
        // column is ignored). SuperAdmin may route rows by Company name and
        // falls back to the first company when the column is blank/unknown.
        var defaultCompanyId = isSuperAdmin
            ? (companies.FirstOrDefault()?.CompanyId ?? 0)
            : Math.Max(callerCompanyId, 0);

        string? Cell(string?[] row, int idx) =>
            idx >= 0 && idx < row.Length ? row[idx]?.Trim() : null;

        var toInsert = new List<Tenant>();
        for (int r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            // Skip blank lines silently — common when users delete the sample row.
            if (row.All(c => string.IsNullOrWhiteSpace(c))) continue;

            var first = Cell(row, iFirst) ?? "";
            var last  = Cell(row, iLast)  ?? "";
            var email = Cell(row, iEmail);
            var phone = Cell(row, iPhone);
            var paci  = Cell(row, iPaci);
            var bldgPaci = Cell(row, iBldgPaci);
            var gov   = Cell(row, iGov);
            var status = Cell(row, iStatus);
            var companyName = Cell(row, iCompany);

            var name = $"{first} {last}".Trim();
            var rr = new ImportResultRow { Row = r + 1, Name = name, Email = email ?? "" };

            // Required-field checks
            if (string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(last))
            { rr.Reason = "missing First Name / Last Name"; results.Add(rr); continue; }

            // Email shape (only if present)
            if (!string.IsNullOrWhiteSpace(email) && !email.Contains('@'))
            { rr.Reason = $"invalid email '{email}'"; results.Add(rr); continue; }

            // Dedupe: email or PACI already on file (case-insensitive)
            if (!string.IsNullOrWhiteSpace(email) && existingEmails.Contains(email))
            { rr.Reason = $"duplicate email '{email}'"; results.Add(rr); continue; }
            if (!string.IsNullOrWhiteSpace(paci) && existingPacis.Contains(paci))
            { rr.Reason = $"duplicate PACI '{paci}'"; results.Add(rr); continue; }

            // Company resolution: name → CompanyId, fall back to first
            // available CompanyId so the FK is satisfied. We log the fallback
            // in the result row so the operator knows.
            int companyId = defaultCompanyId;
            if (isSuperAdmin)
            {
                if (!string.IsNullOrWhiteSpace(companyName)
                    && companyByName.TryGetValue(companyName.ToLowerInvariant(), out var resolved))
                    companyId = resolved;
                else if (!string.IsNullOrWhiteSpace(companyName))
                    rr.Reason = $"company '{companyName}' not found — assigned to default";
            }

            if (companyId <= 0)
            {
                rr.Reason = isSuperAdmin
                    ? "no companies exist in DB — create one first"
                    : "your account isn't linked to a company — ask an admin to set it";
                results.Add(rr); continue;
            }

            var tenant = new Tenant
            {
                FirstName = first,
                LastName = last,
                Email = email,
                Phone = phone,
                PACINumber = paci,
                BuildingPACINumber = bldgPaci,
                GovernmentId = gov,
                Status = string.IsNullOrWhiteSpace(status) ? "Active" : status!,
                CompanyId = companyId,
                CreatedDate = DateTime.UtcNow,
            };
            toInsert.Add(tenant);
            rr.Imported = true;
            results.Add(rr);

            // Update dedupe sets so duplicates WITHIN the file are caught.
            if (!string.IsNullOrWhiteSpace(email)) existingEmails.Add(email);
            if (!string.IsNullOrWhiteSpace(paci))  existingPacis.Add(paci);
        }

        if (toInsert.Count > 0)
        {
            _context.Tenants.AddRange(toInsert);
            await _context.SaveChangesAsync();
        }

        ViewBag.ImportedCount = toInsert.Count;
        ViewBag.SkippedCount = results.Count - toInsert.Count;
        return View("ImportResult", results);
    }

    private static List<string?[]> ReadCsv(Stream stream)
    {
        // Lightweight CSV reader — handles quoted fields and escaped quotes
        // (RFC 4180 lite). No commas-in-quotes support outside the standard.
        using var reader = new StreamReader(stream);
        var lines = new List<string?[]>();
        var sb = new System.Text.StringBuilder();
        var fields = new List<string?>();
        bool inQuotes = false;
        int c;
        while ((c = reader.Read()) != -1)
        {
            var ch = (char)c;
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (reader.Peek() == '"') { sb.Append('"'); reader.Read(); }
                    else inQuotes = false;
                }
                else sb.Append(ch);
            }
            else
            {
                switch (ch)
                {
                    case '"': inQuotes = true; break;
                    case ',':
                        fields.Add(sb.ToString()); sb.Clear(); break;
                    case '\r': break;
                    case '\n':
                        fields.Add(sb.ToString()); sb.Clear();
                        lines.Add(fields.ToArray()); fields.Clear();
                        break;
                    default: sb.Append(ch); break;
                }
            }
        }
        if (sb.Length > 0 || fields.Count > 0)
        {
            fields.Add(sb.ToString());
            lines.Add(fields.ToArray());
        }
        return lines;
    }

    private static List<string?[]> ReadXlsx(Stream stream)
    {
        using var pkg = new ExcelPackage(stream);
        var ws = pkg.Workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("No worksheet found.");
        if (ws.Dimension == null) return new();
        int cols = ws.Dimension.Columns;
        int rowsCount = ws.Dimension.Rows;
        var rows = new List<string?[]>(rowsCount);
        for (int r = 1; r <= rowsCount; r++)
        {
            var row = new string?[cols];
            for (int c = 1; c <= cols; c++)
                row[c - 1] = ws.Cells[r, c].Value?.ToString();
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>
    /// Sprint 5 — bulk Active/Inactive flip for the multi-select toolbar.
    /// Returns { updated: N } so the client can toast accurately. NotFound
    /// IDs are silently skipped (matches per-row toggle behaviour).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkUpdateStatus([FromForm] int[] ids, [FromForm] string status)
    {
        if (ids == null || ids.Length == 0)
            return BadRequest(new { error = "no ids" });
        if (status != "Active" && status != "Inactive")
            return BadRequest(new { error = "status must be Active or Inactive" });

        var tenants = await _context.Tenants.Where(t => ids.Contains(t.TenantId)).ToListAsync();
        var now = DateTime.UtcNow;
        foreach (var t in tenants)
        {
            if (t.Status != status)
            {
                t.Status = status;
                t.ModifiedDate = now;
            }
        }
        await _context.SaveChangesAsync();
        return Json(new { updated = tenants.Count, status });
    }

    /// <summary>
    /// Sprint 5 — bulk soft-delete. Mirrors the single Delete action's
    /// ISoftDeletable handling so financial history stays intact.
    /// Admin-only — same posture as Forget/Restore.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.AnyAdmin)]
    public async Task<IActionResult> BulkArchive([FromForm] int[] ids)
    {
        if (ids == null || ids.Length == 0)
            return BadRequest(new { error = "no ids" });

        var tenants = await _context.Tenants.Where(t => ids.Contains(t.TenantId)).ToListAsync();

        // Never archive a tenant who still has a live (Active / Pending) lease —
        // the lease would point at an invisible tenant. Terminate it first.
        var tenantIds = tenants.Select(t => t.TenantId).ToList();
        var withLiveLease = await _context.Leases
            .Where(l => tenantIds.Contains(l.TenantId)
                && (l.Status == LeaseStatuses.Active || l.Status == LeaseStatuses.Pending))
            .Select(l => l.TenantId)
            .Distinct()
            .ToListAsync();
        if (withLiveLease.Count > 0)
        {
            TempData["ErrorMessage"] =
                $"{withLiveLease.Count} tenant(s) still have an Active or Pending lease and were not archived. Terminate the lease first.";
            return BadRequest(new
            {
                error = "Some tenants still have an Active or Pending lease — terminate the lease first.",
                blocked = withLiveLease
            });
        }

        var now = DateTime.UtcNow;
        var who = User.Identity?.Name;
        foreach (var t in tenants)
        {
            t.IsDeleted = true;
            t.DeletedAt = now;
            t.DeletedBy = who;
        }
        await _context.SaveChangesAsync();
        return Json(new { archived = tenants.Count });
    }

    /// <summary>
    /// SECURITY (audit H1): actions that fetch a tenant with IgnoreQueryFilters()
    /// strip BOTH the soft-delete AND the company scope, so a raw TenantId lookup
    /// would otherwise let a company-A admin Restore / Forget / Export a company-B
    /// tenant. Every such action must re-check ownership through this guard.
    /// SuperAdmin is cross-company by design; everyone else is pinned to the
    /// company on their claim.
    /// </summary>
    /// <summary>
    /// Adds a field-level ModelState error on PACINumber when another tenant
    /// (any company, archived included — the DB index is global) already
    /// carries this Civil ID as PACINumber or GovernmentId. Names the clash
    /// only when the caller may see that tenant.
    /// </summary>
    private async Task AddDuplicateCivilIdErrorAsync(string? civilId, int? excludeTenantId)
    {
        civilId = civilId?.Trim();
        if (string.IsNullOrEmpty(civilId)) return;

        var dup = await _context.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Where(t => (excludeTenantId == null || t.TenantId != excludeTenantId)
                        && (t.PACINumber == civilId || t.GovernmentId == civilId))
            .OrderBy(t => t.IsDeleted).ThenBy(t => t.TenantId)
            .Select(t => new { t.TenantId, t.FirstName, t.LastName, t.CompanyId, t.IsDeleted })
            .FirstOrDefaultAsync();
        if (dup == null) return;

        string who;
        if (!CanAccessCompany(dup.CompanyId))
            who = "a tenant in another company";
        else if (dup.IsDeleted)
            who = $"archived tenant {dup.FirstName} {dup.LastName} (#{dup.TenantId}) — restore that record instead";
        else
            who = $"{dup.FirstName} {dup.LastName} (#{dup.TenantId}) — open that tenant instead of creating a duplicate";

        ModelState.AddModelError(nameof(Tenant.PACINumber), $"Civil ID {civilId} is already registered to {who}.");
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException sql && (sql.Number == 2601 || sql.Number == 2627);

    private bool CanAccessCompany(int? entityCompanyId)
    {
        if (User.IsInRole(Roles.SuperAdmin)) return true;
        return int.TryParse(User.FindFirst("companyId")?.Value, out var cid)
            && entityCompanyId == cid;
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
    /// Sprint 4 — un-archive a soft-deleted tenant. Surfaced only when the
    /// list is loaded with includeArchived=true. Antiforgery + Admin role,
    /// same posture as Forget.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.AnyAdmin)]
    public async Task<IActionResult> Restore(int id)
    {
        var tenant = await _context.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TenantId == id);
        if (tenant == null) return NotFound();
        if (!CanAccessCompany(tenant.CompanyId)) return NotFound();   // audit H1
        if (!tenant.IsDeleted) return Json(new { restored = false, reason = "not archived" });

        tenant.IsDeleted = false;
        tenant.DeletedAt = null;
        tenant.DeletedBy = null;
        tenant.ModifiedDate = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Json(new { restored = true });
    }

    /// <summary>
    /// Re-issue tenant portal credentials. Generates a fresh temporary
    /// password, BCrypt-hashes it, emails it, and forces a change on next
    /// login. Used when the tenant lost the original email or never
    /// received it. Admin-only + antiforgery; every call writes an
    /// AuditLog entry via the credential service.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.AnyAdmin)]
    public async Task<IActionResult> ResendLogin(int id)
    {
        // SECURITY: the credential service takes a raw TenantId and has no
        // company context of its own. Resolve the tenant through the filtered
        // DbSet first so a company-A admin can't reset a company-B login.
        var tenantExists = await _context.Tenants.AnyAsync(t => t.TenantId == id);
        if (!tenantExists) return NotFound();

        var result = await _credentials.IssueAsync(
            tenantId: id,
            issueReason: "Admin resend",
            byUser: User.Identity?.Name ?? "system");

        if (!result.Success)
            return Json(new { success = false, message = result.Message });

        return Json(new
        {
            success = true,
            emailSent = result.EmailSent,
            message = result.Message,
            // Only surfaced when email failed and the admin needs a fallback;
            // the UI must treat this as one-time secret material.
            password = result.EmailSent ? null : result.GeneratedPassword,
        });
    }

    /// <summary>
    /// Sprint 2 — Active ⇄ Inactive without going through Edit.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStatus(int id)
    {
        var t = await _context.Tenants.FirstOrDefaultAsync(x => x.TenantId == id);
        if (t == null) return NotFound();
        t.Status = t.Status == "Active" ? "Inactive" : "Active";
        t.ModifiedDate = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Json(new { status = t.Status });
    }

    /// <summary>
    /// Sprint 1 — Excel export. Filter params mirror the view's filters.
    /// Sprint 5 — ?ids=1,2,3 narrows to a specific selection (used by the
    /// bulk action bar's "Export selected" button). When ids is present
    /// the other filters are ignored — selection wins.
    /// </summary>
    public async Task<IActionResult> ExportExcel(
        string? search, int? propertyId, string? status, string? ids)
    {
        var q = _context.Tenants.AsQueryable();

        // Selection mode: ?ids=1,2,3 short-circuits filters.
        var idList = (ids ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .Where(n => n > 0).Distinct().ToList();
        if (idList.Count > 0)
        {
            q = q.Where(t => idList.Contains(t.TenantId));
            // Skip the other filters; the caller already chose the rows.
            var selectedTenants = await q.OrderBy(t => t.FirstName).ToListAsync();
            return BuildTenantsXlsx(selectedTenants);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(t =>
                ((t.FirstName ?? "") + " " + (t.LastName ?? "")).ToLower().Contains(s) ||
                (t.Email ?? "").ToLower().Contains(s) ||
                (t.Phone ?? "").ToLower().Contains(s) ||
                (t.PACINumber ?? "").ToLower().Contains(s));
        }
        if (!string.IsNullOrEmpty(status)) q = q.Where(t => t.Status == status);

        var tenants = await q.OrderBy(t => t.FirstName).ToListAsync();

        // PropertyId filter is matched via BuildingPACINumber → Property.PACINumber,
        // same approach as the live view. Done client-side after the load.
        if (propertyId.HasValue)
        {
            var paci = await _context.Properties
                .Where(p => p.PropertyId == propertyId)
                .Select(p => p.PACINumber)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrEmpty(paci))
                tenants = tenants.Where(t => t.BuildingPACINumber == paci).ToList();
        }

        return BuildTenantsXlsx(tenants);
    }

    private FileContentResult BuildTenantsXlsx(List<Tenant> tenants)
    {
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("Tenants");
        var headers = new[] { "First Name", "Last Name", "Email", "Phone", "Client PACI", "Building PACI", "Government ID", "Status", "Created" };
        for (int c = 0; c < headers.Length; c++)
        {
            ws.Cells[1, c + 1].Value = headers[c];
            ws.Cells[1, c + 1].Style.Font.Bold = true;
            ws.Cells[1, c + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[1, c + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(99, 102, 241));
            ws.Cells[1, c + 1].Style.Font.Color.SetColor(System.Drawing.Color.White);
        }
        for (int i = 0; i < tenants.Count; i++)
        {
            var t = tenants[i]; var r = i + 2;
            ws.Cells[r, 1].Value = t.FirstName;
            ws.Cells[r, 2].Value = t.LastName;
            ws.Cells[r, 3].Value = t.Email;
            ws.Cells[r, 4].Value = t.Phone;
            ws.Cells[r, 5].Value = t.PACINumber;
            ws.Cells[r, 6].Value = t.BuildingPACINumber;
            ws.Cells[r, 7].Value = t.GovernmentId;
            ws.Cells[r, 8].Value = t.Status;
            ws.Cells[r, 9].Value = t.CreatedDate;
            ws.Cells[r, 9].Style.Numberformat.Format = "yyyy-mm-dd";
        }
        if (ws.Dimension != null) ws.Cells[ws.Dimension.Address].AutoFitColumns();
        var fileName = $"tenants-{DateTime.Today:yyyy-MM-dd}.xlsx";
        return File(pkg.GetAsByteArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    /// <summary>
    /// Sprint 3 — Tenant statement PDF. Single-page summary: contact card,
    /// active leases, last 12 completed payments, outstanding balance.
    /// </summary>
    public async Task<IActionResult> Statement(int id)
    {
        var tenant = await _context.Tenants
            .Include(t => t.Company)
            .FirstOrDefaultAsync(t => t.TenantId == id);
        if (tenant == null) return NotFound();

        var activeLeases = await _context.Leases
            .Where(l => l.TenantId == id && l.Status == LeaseStatuses.Active)
            .Include(l => l.Unit).ThenInclude(u => u.Property)
            .OrderBy(l => l.LeaseStartDate)
            .Select(l => new
            {
                Property = l.Unit.Property.PropertyName,
                Unit = l.Unit.UnitNumber,
                l.LeaseStartDate,
                l.LeaseEndDate,
                l.MonthlyRent,
            })
            .ToListAsync();

        var payments = await _context.Payments
            .Where(p => p.TenantId == id && p.Status == PaymentStatus.Completed)
            .OrderByDescending(p => p.PaymentDate)
            .Take(12)
            .Select(p => new
            {
                p.PaymentDate,
                p.AmountPaid,
                Method = p.PaymentType.ToString(),
                Reference = p.ReceiptNumber ?? p.TransactionReference,
            })
            .ToListAsync();

        var outstanding = await _context.Invoices
            .Where(i => i.TenantId == id && i.Status != "Paid")
            .SumAsync(i => (decimal?)(i.TotalAmount - i.PaidAmount)) ?? 0m;

        var model = new TenantStatementPdfModel
        {
            GeneratedAt = DateTime.UtcNow,
            TenantId = tenant.TenantId,
            TenantName = $"{tenant.FirstName} {tenant.LastName}".Trim(),
            Email = tenant.Email,
            Phone = tenant.Phone,
            PACINumber = tenant.PACINumber,
            CompanyName = tenant.Company?.CompanyName,
            Outstanding = outstanding,
            Currency = "KWD",
            Leases = activeLeases
                .Select(l => new TenantStatementLease(l.Property, l.Unit, l.LeaseStartDate, l.LeaseEndDate, l.MonthlyRent))
                .ToList(),
            RecentPayments = payments
                .Select(p => new TenantStatementPayment(p.PaymentDate, p.AmountPaid, p.Method, p.Reference))
                .ToList(),
        };

        var bytes = _pdf.RenderTenantStatement(model);
        var name = $"tenant-statement-{tenant.TenantId}-{DateTime.Today:yyyy-MM-dd}.pdf";
        return File(bytes, "application/pdf", name);
    }

    #endregion

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var tenant = await _context.Tenants
            .Include(t => t.Company)
            .Include(t => t.Leases)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .Include(t => t.Leases)
                .ThenInclude(l => l.Payments)
            .FirstOrDefaultAsync(m => m.TenantId == id);

        if (tenant == null)
        {
            return NotFound();
        }

        // Get documents related to this tenant
        var tenantDocuments = await _context.Documents
            .Where(d => d.RelatedEntityType == "Tenant" && d.RelatedEntityId == id)
            .OrderByDescending(d => d.UploadedDate)
            .ToListAsync();

        // Get lease documents
        var leaseIds = tenant.Leases.Select(l => l.LeaseId).ToList();
        var leaseDocuments = await _context.Documents
            .Where(d => d.RelatedEntityType == "Lease" && leaseIds.Contains(d.RelatedEntityId ?? 0))
            .OrderByDescending(d => d.UploadedDate)
            .ToListAsync();

        ViewBag.TenantDocuments = tenantDocuments;
        ViewBag.LeaseDocuments = leaseDocuments;

        // Open legal case (if any) for the ⚖️ badge on the header.
        ViewBag.OpenCase = await _context.LegalCases
            .Where(c => c.TenantId == tenant.TenantId)
            .OrderByDescending(c => c.FiledDate).ThenByDescending(c => c.LegalCaseId)
            .FirstOrDefaultAsync();

        // 2026-10 detail redesign — the KPI strip, the per-lease Outstanding
        // column and the "Open invoices" table. Unpaid = not Paid, not
        // Cancelled, and still carrying a balance; ordered oldest-due first.
        var openInvoices = await _context.Invoices.AsNoTracking()
            .Where(i => (i.TenantId == tenant.TenantId || leaseIds.Contains(i.LeaseId))
                        && i.Status != InvoiceStatuses.Paid && i.Status != InvoiceStatuses.Cancelled
                        && i.TotalAmount - i.PaidAmount > 0)
            .OrderBy(i => i.DueDate).ThenBy(i => i.InvoiceId)
            .ToListAsync();
        ViewBag.OpenInvoices = openInvoices;
        ViewBag.OutstandingByLease = openInvoices
            .GroupBy(i => i.LeaseId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.TotalAmount - i.PaidAmount));
        ViewBag.Outstanding = openInvoices.Sum(i => i.TotalAmount - i.PaidAmount);

        // Latest completed payment (date + amount) for the "Last payment" tile.
        var lastPayment = await _context.Payments.AsNoTracking()
            .Where(p => (p.TenantId == tenant.TenantId || leaseIds.Contains(p.LeaseId))
                        && p.Status == PaymentStatus.Completed)
            .OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.PaymentId)
            .Select(p => new { p.PaymentDate, p.AmountPaid })
            .FirstOrDefaultAsync();
        ViewBag.LastPaymentDate = lastPayment?.PaymentDate;
        ViewBag.LastPaymentAmount = lastPayment?.AmountPaid;

        return View(tenant);
    }

    public async Task<IActionResult> Create()
    {
        ViewData["CompanyId"] = new SelectList(await _context.Companies.ToListAsync(), "CompanyId", "CompanyName");
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("FirstName,LastName,PACINumber,BuildingPACINumber,Email,Phone,DateOfBirth,GovernmentId,EmergencyContact,EmergencyPhone,EmploymentStatus")] Tenant tenant,
        int? PropertyId, int? UnitId, List<IFormFile>? Documents, List<string>? DocumentTypes,
        string? InitialPassword = null,
        [FromForm(Name = "__SaveAndAddAnother")] string? saveAndAddAnother = null,
        [FromForm(Name = "CompanyId")] int? postedCompanyId = null)
    {
        // SECURITY: CompanyId is deliberately NOT in the [Bind] list — a non-
        // SuperAdmin must never be able to create a tenant in another company.
        // Only SuperAdmin may honour a posted CompanyId (if the form offers one).
        tenant.CompanyId = 0;
        if (User.IsInRole(Roles.SuperAdmin) && postedCompanyId is > 0)
            tenant.CompanyId = postedCompanyId.Value;

        // Sprint 22 — Company Assignment is no longer asked on the form; it's
        // derived server-side. Resolve it from the logged-in admin's claim…
        if (tenant.CompanyId <= 0)
        {
            var claim = User.FindFirst("companyId")?.Value;
            if (int.TryParse(claim, out var claimedCompanyId) && claimedCompanyId > 0)
                tenant.CompanyId = claimedCompanyId;
        }

        // …then fall back to the user's DB record. The claim is only stamped at
        // login (and only when the user already had a CompanyId), so a session
        // that predates the company link would otherwise be blocked forever.
        if (tenant.CompanyId <= 0)
        {
            var uid = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(uid, out var userId))
            {
                var dbCompanyId = await _context.Users
                    .Where(u => u.UserId == userId)
                    .Select(u => u.CompanyId)
                    .FirstOrDefaultAsync();
                if (dbCompanyId is > 0)
                    tenant.CompanyId = dbCompanyId.Value;
            }
        }

        // The form intentionally doesn't post CompanyId (derived above). The
        // empty hidden field would otherwise leave a spurious
        // "The value '' is invalid." binding error on ModelState — clear it now
        // that the real value is resolved.
        ModelState.Remove(nameof(tenant.CompanyId));

        // Only now surface a clear error if we still couldn't determine the company.
        if (tenant.CompanyId <= 0)
        {
            ModelState.AddModelError("CompanyId",
                "Your account isn't linked to a company. Ask an admin to set your CompanyId on the Users page.");
        }

        // QA finding: a duplicate Civil ID used to surface as the generic
        // "Error creating tenant" from the unique index. Name the clash instead.
        await AddDuplicateCivilIdErrorAsync(tenant.PACINumber, excludeTenantId: null);

        if (ModelState.IsValid)
        {
            try
            {
                tenant.CreatedDate = DateTime.UtcNow;
                tenant.Status = tenant.Status ?? "Active";
                // Tenant login matches on GovernmentId (the Civil ID). Many
                // tenants are onboarded without a separate Government ID, so
                // default it to the Civil ID (PACINumber) entered on the form —
                // otherwise the tenant could never log in.
                if (string.IsNullOrWhiteSpace(tenant.GovernmentId))
                    tenant.GovernmentId = tenant.PACINumber;
                _context.Add(tenant);
                await _context.SaveChangesAsync();

                // Leases are created ONLY from the Leasing module (LeasesController).
                // Creating a tenant no longer auto-assigns a unit or spins up a lease —
                // both the explicit-UnitId path and the Building-PACI auto-assign path
                // were removed. The tenant's unit/lease is set up later via a Lease.
                TempData["SuccessMessage"] = "Tenant created successfully!";

                // Handle document uploads
                if (Documents != null && Documents.Any() && DocumentTypes != null)
                {
                    await UploadDocuments(tenant.TenantId, Documents, DocumentTypes, "Tenant");
                }

                // Password. The admin can type the tenant's password directly (no
                // email needed). If left blank, auto-generate one instead. Either
                // way the login is shown to the admin to hand over, and the tenant
                // can change it later from the app.
                if (!string.IsNullOrWhiteSpace(InitialPassword))
                {
                    tenant.Password = BCrypt.Net.BCrypt.HashPassword(InitialPassword.Trim());
                    // Admin chose it — don't force a change on first login.
                    tenant.MustChangePasswordOnNextLogin = false;
                    await _context.SaveChangesAsync();
                    TempData["NewTenantCredential"] = $"{tenant.GovernmentId}|{InitialPassword.Trim()}";
                    TempData["SuccessMessage"] += " Login password set.";
                }
                else
                {
                    try
                    {
                        var credResult = await _credentials.IssueAsync(
                            tenantId: tenant.TenantId,
                            issueReason: "Initial onboarding",
                            byUser: User.Identity?.Name ?? "system");
                        if (credResult.Success)
                        {
                            TempData["SuccessMessage"] += $" Login: {credResult.Message}";
                            // No email on file (the common case) — hand the login to
                            // the admin. Stored in a dedicated key so the view renders
                            // a persistent, copyable box (NOT the auto-dismissing alert).
                            if (!credResult.EmailSent && !string.IsNullOrEmpty(credResult.GeneratedPassword))
                            {
                                TempData["NewTenantCredential"] =
                                    $"{tenant.GovernmentId}|{credResult.GeneratedPassword}";
                            }
                        }
                        else
                        {
                            TempData["WarningMessage"] = $"Tenant created, but credential issue failed: {credResult.Message}";
                        }
                    }
                    catch (Exception credEx)
                    {
                        _logger.LogError(credEx, "Tenant {Id} created but credential issue threw", tenant.TenantId);
                        TempData["WarningMessage"] =
                            "Tenant created, but the login could not be generated. Use 'Resend login' from the tenant row.";
                    }
                }

                // Sprint 23 — "Save & add another" sends operator back to a
                // fresh Create form. Index is the normal landing.
                if (string.Equals(saveAndAddAnother, "true", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["TenantCreated"] = $"{tenant.FirstName} {tenant.LastName}".Trim();
                    return RedirectToAction(nameof(Create));
                }
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateException dbEx) when (IsUniqueViolation(dbEx))
            {
                // Race with another operator, or a clash the pre-check can't see.
                _logger.LogWarning(dbEx, "Unique violation creating tenant {Name}", $"{tenant.FirstName} {tenant.LastName}".Trim());
                _context.Entry(tenant).State = EntityState.Detached;
                ModelState.AddModelError(nameof(Tenant.PACINumber),
                    $"Civil ID {tenant.PACINumber} is already registered to another tenant. Open that tenant instead of creating a duplicate.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating tenant {Name}", $"{tenant.FirstName} {tenant.LastName}".Trim());
                ModelState.AddModelError("", "Error creating tenant. Please check the details and try again.");
            }
        }
        ViewData["CompanyId"] = new SelectList(await _context.Companies.ToListAsync(), "CompanyId", "CompanyName", tenant.CompanyId);
        return View(tenant);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var tenant = await _context.Tenants.FindAsync(id);
        if (tenant == null)
        {
            return NotFound();
        }
        ViewData["CompanyId"] = new SelectList(await _context.Companies.ToListAsync(), "CompanyId", "CompanyName", tenant.CompanyId);
        return View(tenant);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("TenantId,FirstName,LastName,PACINumber,BuildingPACINumber,Email,Phone,DateOfBirth,GovernmentId,EmergencyContact,EmergencyPhone,EmploymentStatus,Status")] Tenant tenant)
    {
        if (id != tenant.TenantId)
        {
            return NotFound();
        }

        // The form still posts a CompanyId <select>; it is intentionally not
        // bound (company is never editable here) so drop any binding noise.
        ModelState.Remove(nameof(Tenant.CompanyId));

        // LOAD-THEN-COPY. `_context.Update(postedModel)` marked EVERY column
        // modified, so anything the form didn't post (Password, CompanyId,
        // MustChangePasswordOnNextLogin, DeletedAt, …) was overwritten with
        // defaults — wiping tenant logins on each edit. Loading the tracked row
        // through the filtered DbSet also enforces the company scope.
        var existing = await _context.Tenants.FirstOrDefaultAsync(t => t.TenantId == id);
        if (existing == null)
        {
            return NotFound();
        }

        // Only check when the Civil ID actually changed: 23 imported tenants
        // still share the placeholder ID and must stay editable until the
        // owner supplies real ones.
        if (!string.Equals(existing.PACINumber?.Trim(), tenant.PACINumber?.Trim(), StringComparison.Ordinal))
            await AddDuplicateCivilIdErrorAsync(tenant.PACINumber, excludeTenantId: id);

        if (ModelState.IsValid)
        {
            try
            {
                existing.FirstName = tenant.FirstName;
                existing.LastName = tenant.LastName;
                existing.PACINumber = tenant.PACINumber;
                existing.BuildingPACINumber = tenant.BuildingPACINumber;
                existing.Email = tenant.Email;
                existing.Phone = tenant.Phone;
                existing.DateOfBirth = tenant.DateOfBirth;
                existing.GovernmentId = tenant.GovernmentId;
                existing.EmergencyContact = tenant.EmergencyContact;
                existing.EmergencyPhone = tenant.EmergencyPhone;
                existing.EmploymentStatus = tenant.EmploymentStatus;
                if (!string.IsNullOrWhiteSpace(tenant.Status))
                    existing.Status = tenant.Status;
                existing.ModifiedDate = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Tenant updated successfully!";
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!TenantExists(tenant.TenantId))
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
        // Re-render with the real (unbound) company so the dropdown stays honest.
        tenant.CompanyId = existing.CompanyId;
        ViewData["CompanyId"] = new SelectList(await _context.Companies.ToListAsync(), "CompanyId", "CompanyName", tenant.CompanyId);
        return View(tenant);
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var tenant = await _context.Tenants
            .Include(t => t.Company)
            .Include(t => t.Leases)
            .FirstOrDefaultAsync(m => m.TenantId == id);

        if (tenant == null)
        {
            return NotFound();
        }

        return View(tenant);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var tenant = await _context.Tenants.FindAsync(id);
        if (tenant != null)
        {
            // Never archive a tenant who still has a live (Active / Pending)
            // lease — the lease would point at an invisible tenant.
            var hasLiveLease = await _context.Leases.AnyAsync(l =>
                l.TenantId == id
                && (l.Status == LeaseStatuses.Active || l.Status == LeaseStatuses.Pending));
            if (hasLiveLease)
            {
                TempData["ErrorMessage"] =
                    "This tenant still has an Active or Pending lease. Terminate the lease before archiving the tenant.";
                return RedirectToAction(nameof(Index));
            }

            // Item 10: soft-delete. Tenants accumulate legally-required
            // history (leases, payments, receipts) so we never hard-delete —
            // GDPR right-to-erasure can be honoured via the Forget action.
            tenant.IsDeleted = true;
            tenant.DeletedAt = DateTime.UtcNow;
            tenant.DeletedBy = User.Identity?.Name;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Tenant archived (soft-delete).";
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Item 10 — GDPR data export. Returns a JSON dump of every row tied to
    /// the tenant: profile, leases, payments, invoices, maintenance requests,
    /// documents (metadata only). The tenant has the right to receive this
    /// on request under both GDPR and Kuwait's data-protection law.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Export(int id)
    {
        var tenant = await _context.Tenants
            .IgnoreQueryFilters()
            .Include(t => t.Leases).ThenInclude(l => l.Invoices)
            .Include(t => t.Payments)
            .Include(t => t.MaintenanceRequests)
            .FirstOrDefaultAsync(t => t.TenantId == id);
        if (tenant == null) return NotFound();
        if (!CanAccessCompany(tenant.CompanyId)) return NotFound();   // audit H1

        var dump = new
        {
            exportedAt = DateTime.UtcNow,
            tenant = new
            {
                tenant.TenantId, tenant.FirstName, tenant.LastName, tenant.Email,
                tenant.Phone, tenant.GovernmentId, tenant.PACINumber, tenant.DateOfBirth,
                tenant.Status, tenant.CreatedDate
            },
            leases = tenant.Leases.Select(l => new { l.LeaseId, l.LeaseStartDate, l.LeaseEndDate, l.MonthlyRent, l.Status }),
            payments = tenant.Payments.Select(p => new { p.PaymentId, p.PaymentDate, p.AmountPaid, p.Status, p.ReceiptNumber }),
            maintenanceRequests = tenant.MaintenanceRequests.Select(m => new { m.RequestId, m.Category, m.Description, m.Status, m.RequestDate })
        };
        return Json(dump);
    }

    /// <summary>
    /// Item 10 — GDPR right-to-erasure. Anonymises PII while keeping the row
    /// for financial-history integrity. Names become "[REDACTED]", contact
    /// fields are cleared. The tenant cannot log in afterwards.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.AnyAdmin)]
    public async Task<IActionResult> Forget(int id)
    {
        var tenant = await _context.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TenantId == id);
        if (tenant == null) return NotFound();
        if (!CanAccessCompany(tenant.CompanyId)) return NotFound();   // audit H1 — irreversible PII wipe

        tenant.FirstName = "[REDACTED]";
        tenant.LastName = "[REDACTED]";
        tenant.Email = $"redacted+{tenant.TenantId}@example.invalid";
        tenant.Phone = null;
        tenant.GovernmentId = null;
        tenant.PACINumber = null;
        tenant.DateOfBirth = null;
        tenant.Password = null;
        tenant.IsDeleted = true;
        tenant.DeletedAt = DateTime.UtcNow;
        tenant.DeletedBy = User.Identity?.Name;
        tenant.Status = "Inactive";
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Tenant #{id} PII redacted. Financial history preserved.";
        return RedirectToAction(nameof(Index));
    }

    private bool TenantExists(int id)
    {
        return _context.Tenants.Any(e => e.TenantId == id);
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

            var saved = new List<Document>();
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
                    saved.Add(document);
                }
            }

            if (saved.Count == 0) return;
            await _context.SaveChangesAsync();

            // Mirror DocumentsController.Upload: stamp a DocumentLink per file
            // (after SaveChanges so DocumentId is set). Without the link row,
            // DocumentsController.CanAccessDocumentAsync can't resolve the
            // document's company and the file is invisible to other admins.
            foreach (var document in saved)
            {
                _context.DocumentLinks.Add(new DocumentLink
                {
                    DocumentId = document.DocumentId,
                    EntityType = entityType,
                    EntityId = entityId,
                    LinkedAt = DateTime.UtcNow,
                    LinkedBy = User.Identity?.Name
                });
            }
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading documents for {EntityType} {EntityId}", entityType, entityId);
            // Don't swallow silently — the record itself was saved, but the
            // operator needs to know the attachments didn't make it.
            TempData["WarningMessage"] =
                "Saved, but one or more documents could not be uploaded. Attach them again from the Details page.";
        }
    }
}
