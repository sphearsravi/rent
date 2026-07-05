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
public class TenantsController : Controller
{
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
            var q = includeArchived
                ? _context.Tenants.IgnoreQueryFilters()
                : _context.Tenants.AsQueryable();

            var tenants = await q
                .AsNoTracking()
                .OrderByDescending(t => t.CreatedDate)
                .ToListAsync();

            // Load properties separately to match via PACI (read-only → no tracking)
            var properties = await _context.Properties.AsNoTracking().ToListAsync();

            ViewBag.Properties = properties;
            ViewBag.IncludeArchived = includeArchived;
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
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Any)]
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
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Any, VaryByQueryKeys = new[] { "ids" })]
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
            ViewBag.ErrorMessage = "Couldn't parse the file: " + ex.Message;
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
        var existingEmails = new HashSet<string>(
            await _context.Tenants.IgnoreQueryFilters()
                .Where(t => t.Email != null && t.Email != "")
                .Select(t => t.Email!.ToLower()).ToListAsync(),
            StringComparer.OrdinalIgnoreCase);
        var existingPacis = new HashSet<string>(
            await _context.Tenants.IgnoreQueryFilters()
                .Where(t => t.PACINumber != null && t.PACINumber != "")
                .Select(t => t.PACINumber!).ToListAsync(),
            StringComparer.OrdinalIgnoreCase);
        var companies = await _context.Companies
            .Select(c => new { c.CompanyId, c.CompanyName })
            .ToListAsync();
        var companyByName = companies.ToDictionary(c => c.CompanyName.ToLowerInvariant(), c => c.CompanyId);
        var defaultCompanyId = companies.FirstOrDefault()?.CompanyId ?? 0;

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
            if (!string.IsNullOrWhiteSpace(companyName)
                && companyByName.TryGetValue(companyName.ToLowerInvariant(), out var resolved))
                companyId = resolved;
            else if (!string.IsNullOrWhiteSpace(companyName))
                rr.Reason = $"company '{companyName}' not found — assigned to default";

            if (companyId == 0)
            { rr.Reason = "no companies exist in DB — create one first"; results.Add(rr); continue; }

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

        return View(tenant);
    }

    public async Task<IActionResult> Create()
    {
        ViewData["CompanyId"] = new SelectList(await _context.Companies.ToListAsync(), "CompanyId", "CompanyName");
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("FirstName,LastName,PACINumber,BuildingPACINumber,Email,Phone,DateOfBirth,GovernmentId,CompanyId,EmergencyContact,EmergencyPhone,EmploymentStatus")] Tenant tenant,
        int? PropertyId, int? UnitId, List<IFormFile>? Documents, List<string>? DocumentTypes,
        string? InitialPassword = null,
        [FromForm(Name = "__SaveAndAddAnother")] string? saveAndAddAnother = null)
    {
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
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error creating tenant: {ex.Message}");
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
    public async Task<IActionResult> Edit(int id, [Bind("TenantId,FirstName,LastName,PACINumber,BuildingPACINumber,Email,Phone,DateOfBirth,GovernmentId,CompanyId,EmergencyContact,EmergencyPhone,EmploymentStatus,Status,CreatedDate")] Tenant tenant)
    {
        if (id != tenant.TenantId)
        {
            return NotFound();
        }

        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(tenant);
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

            for (int i = 0; i < documents.Count; i++)
            {
                var file = documents[i];
                if (file != null && file.Length > 0 && i < documentTypes.Count && !string.IsNullOrEmpty(documentTypes[i]))
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
                }
            }

            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading documents for {EntityType} {EntityId}", entityType, entityId);
        }
    }
}
