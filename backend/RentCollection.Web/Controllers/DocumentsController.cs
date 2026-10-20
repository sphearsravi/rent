using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.Controllers;

[Authorize(Roles = Roles.Operational)]
public class DocumentsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        ApplicationDbContext context,
        IWebHostEnvironment environment,
        ILogger<DocumentsController> logger)
    {
        _context = context;
        _environment = environment;
        _logger = logger;
    }

    // Allowed extensions/MIME for document uploads. Keep narrow — any executable
    // or script type here is a remote-code-execution risk if it's ever served back.
    private static readonly string[] AllowedExtensions =
        { ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx", ".xls", ".xlsx" };
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/jpeg", "image/png",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    };
    private const long MaxUploadBytes = 5 * 1024 * 1024; // 5 MB

    #region Index — list, filter, name resolution, company scope

    /// <summary>
    /// Documents listing with optional filters:
    ///   ?type=Tenant|Property|Lease|MaintenanceRequest
    ///   ?entityId=N  (when set, narrows to that specific entity)
    ///   ?search=…    (matches document name or linked entity name)
    /// Company scope: a document is visible if it has at least one link to an
    /// entity belonging to the caller's CurrentCompanyId. Unlinked documents
    /// are visible only to their uploader (so a stray upload isn't lost but
    /// doesn't leak across companies either). SuperAdmins bypass the scope.
    /// </summary>
    public async Task<IActionResult> Index(string? type, int? entityId, string? search)
    {
        try
        {
            // Company scope is applied IN SQL. The previous version loaded every
            // company's documents and filtered in memory — slow, and a scoped
            // admin with no companyId claim skipped the filter entirely. A doc
            // is visible if at least one link resolves to an entity the caller
            // can see through the filtered Tenants/Properties sets (Leases via
            // their Tenant, MaintenanceRequests via their Property), or it is
            // unlinked and the caller uploaded it. SuperAdmin / migrations
            // bypass (BypassCompanyFilter). A scoped admin with no company
            // claim has CurrentCompanyId == null, so the filtered sets match
            // nothing → deny-by-default.
            IQueryable<Document> q = _context.Documents.Include(d => d.Links);

            if (!_context.BypassCompanyFilter)
            {
                var me = User.Identity?.Name ?? "";
                q = q.Where(d =>
                    d.Links.Any(l =>
                        (l.EntityType == DocumentLinkEntityTypes.Tenant
                            && _context.Tenants.Any(t => t.TenantId == l.EntityId))
                        || (l.EntityType == DocumentLinkEntityTypes.Property
                            && _context.Properties.Any(p => p.PropertyId == l.EntityId))
                        || (l.EntityType == DocumentLinkEntityTypes.Lease
                            && _context.Leases.Any(le => le.LeaseId == l.EntityId
                                && _context.Tenants.Any(t => t.TenantId == le.TenantId)))
                        || (l.EntityType == DocumentLinkEntityTypes.MaintenanceRequest
                            && _context.MaintenanceRequests.Any(m => m.RequestId == l.EntityId
                                && _context.Properties.Any(p => p.PropertyId == m.PropertyId))))
                    || (!d.Links.Any() && d.UploadedBy == me));
            }

            // Type filter — keep documents that have at least one matching link.
            // Special case: "Unlinked" shows docs with zero links.
            if (!string.IsNullOrEmpty(type))
            {
                q = type == "Unlinked"
                    ? q.Where(d => !d.Links.Any())
                    : q.Where(d => d.Links.Any(l => l.EntityType == type
                        && (!entityId.HasValue || l.EntityId == entityId.Value)));
            }

            var raw = await q
                .OrderByDescending(d => d.UploadedDate)
                .ToListAsync();

            // Resolve names for every distinct (EntityType, EntityId) pair the
            // visible docs reference — one batched query per entity type, even
            // if 200 docs link 50 distinct tenants.
            var nameMap = await BuildEntityNameMapAsync(raw.SelectMany(d => d.Links));

            // Text search — match doc name, description, or any linked entity's name.
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLowerInvariant();
                raw = raw.Where(d =>
                    (d.DocumentName ?? "").ToLowerInvariant().Contains(s)
                    || (d.Description ?? "").ToLowerInvariant().Contains(s)
                    || d.Links.Any(l => (nameMap.GetName(l.EntityType, l.EntityId) ?? "").ToLowerInvariant().Contains(s))
                ).ToList();
            }

            ViewBag.NameMap = nameMap;
            ViewBag.FilterType = type;
            ViewBag.FilterEntityId = entityId;
            ViewBag.FilterSearch = search;
            return View(raw);
        }
        catch (Exception ex)
        {
            // Don't make a query failure look identical to "no documents".
            _logger.LogError(ex, "Error loading documents index");
            TempData["ErrorMessage"] = "Could not load documents. Please try again.";
            return View(new List<Document>());
        }
    }

    /// <summary>
    /// JSON list of documents linked to one entity. Powers the embedded
    /// Documents section on /Properties/Details (lazy-load on render).
    /// </summary>
    public async Task<IActionResult> ByEntity(string type, int id)
    {
        if (string.IsNullOrEmpty(type) || !DocumentLinkEntityTypes.All.Contains(type))
            return BadRequest(new { error = "type required" });
        // Company scope: the target entity itself must be the caller's.
        if (!await CanAccessEntityAsync(type, id)) return NotFound();

        var docs = await _context.Documents
            .Where(d => d.Links.Any(l => l.EntityType == type && l.EntityId == id))
            .OrderByDescending(d => d.UploadedDate)
            .Select(d => new
            {
                d.DocumentId,
                d.DocumentName,
                d.DocumentType,
                d.FileSize,
                d.Description,
                d.UploadedBy,
                d.UploadedDate,
                LinkId = d.Links.Where(l => l.EntityType == type && l.EntityId == id)
                                .Select(l => (int?)l.DocumentLinkId).FirstOrDefault(),
            })
            .ToListAsync();

        return Json(docs);
    }

    #endregion

    #region Upload — single + multi-link

    public async Task<IActionResult> Upload(string? type, int? entityId)
    {
        // ?type=&entityId= pre-selects a link on the form (used by deep links
        // from Properties/Details "Upload" button).
        ViewBag.PreType = type;
        ViewBag.PreEntityId = entityId;
        await PopulateEntityDropdowns();
        return View();
    }

    /// <summary>
    /// Accepts a single file plus N (entityType, entityId) pairs. Each pair
    /// becomes a DocumentLink. Backward-compatible: if only one pair is
    /// supplied via the legacy single-link inputs (relatedEntityType +
    /// relatedEntityId), the loose Document.RelatedEntityType/Id columns
    /// are also stamped so older readers keep working.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(
        IFormFile file,
        string documentType,
        string[] entityType,
        int[] entityId,
        string? description,
        string? relatedEntityType,
        int? relatedEntityId)
    {
        if (file == null || file.Length == 0)
        {
            TempData["ErrorMessage"] = "Please select a file to upload.";
            await PopulateEntityDropdowns();
            return View();
        }

        if (file.Length > MaxUploadBytes)
        {
            TempData["ErrorMessage"] = "File exceeds the 5 MB upload limit.";
            await PopulateEntityDropdowns();
            return View();
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension) || !AllowedContentTypes.Contains(file.ContentType))
        {
            TempData["ErrorMessage"] = "File type not allowed. Permitted: PDF, JPG, PNG, DOC(X), XLS(X).";
            await PopulateEntityDropdowns();
            return View();
        }

        try
        {
            var uploadsPath = Path.Combine(_environment.WebRootPath, "uploads", "documents");
            if (!Directory.Exists(uploadsPath))
            {
                Directory.CreateDirectory(uploadsPath);
            }

            // Server-generated filename — never trust the user-supplied one. The
            // original is preserved as DocumentName for display, not on disk.
            var fileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(uploadsPath, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Build the link set from the multi-link inputs, with the legacy
            // single-link form as a fallback when callers only sent one.
            var links = MergeLinkInputs(entityType, entityId, relatedEntityType, relatedEntityId);

            var document = new Document
            {
                DocumentName = Path.GetFileName(file.FileName), // strip any path
                DocumentType = documentType,
                FilePath = Path.Combine("uploads", "documents", fileName),
                FileSize = file.Length,
                // Loose columns kept in sync with the FIRST link for legacy readers.
                RelatedEntityType = links.FirstOrDefault().Type,
                RelatedEntityId = links.FirstOrDefault().Id,
                Description = description,
                UploadedBy = User.Identity?.Name ?? "system",
                UploadedDate = DateTime.UtcNow
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync();

            // Now stamp the link rows. SaveChanges first so DocumentId is set.
            foreach (var (t, i) in links)
            {
                _context.DocumentLinks.Add(new DocumentLink
                {
                    DocumentId = document.DocumentId,
                    EntityType = t,
                    EntityId = i,
                    LinkedAt = DateTime.UtcNow,
                    LinkedBy = User.Identity?.Name
                });
            }
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Document uploaded with {links.Count} link(s).";

            // Bounce back to the originating entity if upload came from there.
            if (links.Count == 1 && links[0].Type == DocumentLinkEntityTypes.Property)
                return RedirectToAction("Details", "Properties", new { id = links[0].Id });
            return RedirectToAction(nameof(Index));
        }
        catch (Exception)
        {
            // Do not surface raw exception text to the user — it can leak DB schema,
            // paths, or stack traces. The exception is still logged by the framework.
            TempData["ErrorMessage"] = "Error uploading document.";
            await PopulateEntityDropdowns();
            return View();
        }
    }

    private static List<(string Type, int Id)> MergeLinkInputs(
        string[]? types, int[]? ids, string? legacyType, int? legacyId)
    {
        var list = new List<(string Type, int Id)>();
        if (types != null && ids != null)
        {
            var n = Math.Min(types.Length, ids.Length);
            for (int i = 0; i < n; i++)
            {
                if (!string.IsNullOrWhiteSpace(types[i])
                    && ids[i] > 0
                    && DocumentLinkEntityTypes.All.Contains(types[i]))
                {
                    list.Add((types[i], ids[i]));
                }
            }
        }
        // Legacy single-link fallback (used by callers that still POST
        // relatedEntityType/relatedEntityId rather than the arrays).
        if (list.Count == 0
            && !string.IsNullOrWhiteSpace(legacyType)
            && legacyId.HasValue
            && legacyId.Value > 0
            && DocumentLinkEntityTypes.All.Contains(legacyType))
        {
            list.Add((legacyType, legacyId.Value));
        }
        // De-duplicate within a single upload (operator picked the same pair twice).
        return list.Distinct().ToList();
    }

    private async Task PopulateEntityDropdowns()
    {
        ViewData["Tenants"] = await _context.Tenants
            .Select(t => new { t.TenantId, Name = t.FirstName + " " + t.LastName + " - " + t.PACINumber })
            .ToListAsync();

        ViewData["Properties"] = await _context.Properties
            .Select(p => new { p.PropertyId, Name = p.PropertyName + " - " + p.PACINumber })
            .ToListAsync();

        ViewData["Leases"] = await _context.Leases
            .Include(l => l.Tenant)
            .Include(l => l.Unit)
                .ThenInclude(u => u.Property)
            .Select(l => new {
                l.LeaseId,
                Name = l.Tenant.FirstName + " " + l.Tenant.LastName + " - " + l.Unit.Property.PropertyName + " Unit " + l.Unit.UnitNumber
            })
            .ToListAsync();

        ViewData["MaintenanceRequests"] = await _context.MaintenanceRequests
            .Include(m => m.Property)
            .Include(m => m.Unit)
            .Select(m => new {
                m.RequestId,
                Name = "#" + m.RequestId + " - " + m.Property.PropertyName + (m.Unit != null ? " Unit " + m.Unit.UnitNumber : "") + " - " + m.Category
            })
            .ToListAsync();
    }

    #endregion

    #region Link / Unlink

    /// <summary>
    /// Add a new link to an existing document. Idempotent — the unique index
    /// on (DocumentId, EntityType, EntityId) means duplicate POSTs are a
    /// no-op via the EXISTS check.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Link(int docId, string type, int entityId)
    {
        if (string.IsNullOrWhiteSpace(type) || !DocumentLinkEntityTypes.All.Contains(type))
            return BadRequest(new { error = "invalid type" });
        if (entityId <= 0) return BadRequest(new { error = "invalid entityId" });

        var doc = await _context.Documents.FindAsync(docId);
        if (doc == null) return NotFound();
        // Company scope: both the document AND the target entity must be the
        // caller's — otherwise a scoped admin could attach their doc to another
        // company's tenant (or pull another company's doc into their scope).
        if (!await CanAccessDocumentAsync(doc)) return NotFound();
        if (!await CanAccessEntityAsync(type, entityId)) return NotFound();

        var exists = await _context.DocumentLinks.AnyAsync(l =>
            l.DocumentId == docId && l.EntityType == type && l.EntityId == entityId);
        if (exists) return Json(new { linked = false, reason = "already linked" });

        _context.DocumentLinks.Add(new DocumentLink
        {
            DocumentId = docId,
            EntityType = type,
            EntityId = entityId,
            LinkedAt = DateTime.UtcNow,
            LinkedBy = User.Identity?.Name
        });
        await _context.SaveChangesAsync();
        return Json(new { linked = true });
    }

    /// <summary>
    /// Drop a single link without touching the underlying document or file.
    /// Used by the inline "unlink" affordance on entity Details pages.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlink(int linkId)
    {
        var link = await _context.DocumentLinks.FindAsync(linkId);
        if (link == null) return NotFound();
        // Company scope: the document must be accessible AND the link must
        // point at an entity in the caller's company.
        var doc = await _context.Documents.FindAsync(link.DocumentId);
        if (doc == null || !await CanAccessDocumentAsync(doc)) return NotFound();
        if (!await CanAccessEntityAsync(link.EntityType, link.EntityId)) return NotFound();
        _context.DocumentLinks.Remove(link);
        await _context.SaveChangesAsync();
        return Json(new { unlinked = true });
    }

    #endregion

    #region Download / Delete

    /// <summary>
    /// SECURITY (audit C7): Download/Delete previously fetched a Document by raw id
    /// with NO ownership/company check (Index scopes carefully, these did not), so
    /// an Operational user of company A could enumerate ids and pull/destroy any
    /// company's tenant IDs, contracts, and bank-transfer proofs. This mirrors the
    /// exact Index scoping: a scoped admin may access a doc only if one of its links
    /// is in their company, or (for an unlinked doc) they uploaded it. SuperAdmin /
    /// migrations bypass.
    /// </summary>
    private async Task<bool> CanAccessDocumentAsync(Document document)
    {
        if (_context.BypassCompanyFilter || !_context.CurrentCompanyId.HasValue)
            return true;
        var myCompany = _context.CurrentCompanyId.Value;
        var me = User.Identity?.Name ?? "";
        var links = await _context.DocumentLinks
            .Where(l => l.DocumentId == document.DocumentId).ToListAsync();
        if (links.Count == 0)
            return document.UploadedBy == me;
        var nameMap = await BuildEntityNameMapAsync(links);
        return links.Any(l => nameMap.GetCompanyId(l.EntityType, l.EntityId) == myCompany);
    }

    /// <summary>
    /// Company check for a single (entityType, entityId) target — used by
    /// Link / Unlink / ByEntity so a scoped admin can't reach another
    /// company's entity by id. Reuses the name-map resolver (which already
    /// knows each entity type's company path). SuperAdmin / migrations
    /// bypass; a scoped admin with no company claim is denied.
    /// </summary>
    private async Task<bool> CanAccessEntityAsync(string type, int id)
    {
        if (_context.BypassCompanyFilter) return true;
        if (!_context.CurrentCompanyId.HasValue) return false;
        var map = await BuildEntityNameMapAsync(
            new[] { new DocumentLink { EntityType = type, EntityId = id } });
        return map.GetCompanyId(type, id) == _context.CurrentCompanyId.Value;
    }

    public async Task<IActionResult> Download(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var document = await _context.Documents.FindAsync(id);
        if (document == null)
        {
            return NotFound();
        }
        if (!await CanAccessDocumentAsync(document)) return NotFound();   // audit C7

        var filePath = Path.Combine(_environment.WebRootPath, document.FilePath);
        if (!System.IO.File.Exists(filePath))
        {
            TempData["ErrorMessage"] = "File not found on server.";
            return RedirectToAction(nameof(Index));
        }

        var memory = new MemoryStream();
        using (var stream = new FileStream(filePath, FileMode.Open))
        {
            await stream.CopyToAsync(memory);
        }
        memory.Position = 0;

        return File(memory, GetContentType(document.DocumentName), document.DocumentName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var document = await _context.Documents.FindAsync(id);
        if (document != null)
        {
            if (!await CanAccessDocumentAsync(document)) return NotFound();   // audit C7

            // Delete file from disk
            var filePath = Path.Combine(_environment.WebRootPath, document.FilePath);
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }

            // DocumentLinks cascade via the FK (OnDelete: Cascade in DbContext),
            // so we don't need to remove them explicitly here.
            _context.Documents.Remove(document);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Document deleted successfully!";
        }

        return RedirectToAction(nameof(Index));
    }

    #endregion

    private async Task<EntityNameMap> BuildEntityNameMapAsync(IEnumerable<DocumentLink> links)
    {
        var byType = links.GroupBy(l => l.EntityType)
            .ToDictionary(g => g.Key, g => g.Select(l => l.EntityId).Distinct().ToList());

        var map = new EntityNameMap();

        if (byType.TryGetValue(DocumentLinkEntityTypes.Tenant, out var tenantIds) && tenantIds.Count > 0)
        {
            var rows = await _context.Tenants.IgnoreQueryFilters()
                .Where(t => tenantIds.Contains(t.TenantId))
                .Select(t => new { t.TenantId, t.FirstName, t.LastName, t.CompanyId })
                .ToListAsync();
            foreach (var r in rows)
                map.Add(DocumentLinkEntityTypes.Tenant, r.TenantId,
                    $"{r.FirstName} {r.LastName}".Trim(),
                    $"/Tenants/Details/{r.TenantId}", r.CompanyId);
        }
        if (byType.TryGetValue(DocumentLinkEntityTypes.Property, out var propIds) && propIds.Count > 0)
        {
            var rows = await _context.Properties.IgnoreQueryFilters()
                .Where(p => propIds.Contains(p.PropertyId))
                .Select(p => new { p.PropertyId, p.PropertyName, p.CompanyId })
                .ToListAsync();
            foreach (var r in rows)
                map.Add(DocumentLinkEntityTypes.Property, r.PropertyId,
                    r.PropertyName, $"/Properties/Details/{r.PropertyId}", r.CompanyId);
        }
        if (byType.TryGetValue(DocumentLinkEntityTypes.Lease, out var leaseIds) && leaseIds.Count > 0)
        {
            var rows = await _context.Leases
                .Include(l => l.Tenant)
                .Include(l => l.Unit).ThenInclude(u => u.Property)
                .Where(l => leaseIds.Contains(l.LeaseId))
                .Select(l => new
                {
                    l.LeaseId,
                    Display = (l.Tenant.FirstName + " " + l.Tenant.LastName).Trim()
                              + " @ " + l.Unit.Property.PropertyName + " #" + l.Unit.UnitNumber,
                    CompanyId = (int?)l.Tenant.CompanyId,
                })
                .ToListAsync();
            foreach (var r in rows)
                map.Add(DocumentLinkEntityTypes.Lease, r.LeaseId,
                    r.Display, $"/Leases/Details/{r.LeaseId}", r.CompanyId);
        }
        if (byType.TryGetValue(DocumentLinkEntityTypes.MaintenanceRequest, out var mrIds) && mrIds.Count > 0)
        {
            var rows = await _context.MaintenanceRequests
                .Include(m => m.Property)
                .Where(m => mrIds.Contains(m.RequestId))
                .Select(m => new
                {
                    m.RequestId,
                    Display = "#" + m.RequestId + " · " + m.Category,
                    CompanyId = (int?)m.Property.CompanyId,
                })
                .ToListAsync();
            foreach (var r in rows)
                map.Add(DocumentLinkEntityTypes.MaintenanceRequest, r.RequestId,
                    r.Display, $"/Maintenance/Details/{r.RequestId}", r.CompanyId);
        }
        return map;
    }

    /// <summary>
    /// Resolved entity-name + URL + companyId for every (EntityType, EntityId)
    /// referenced by the current page's documents. Built once per request.
    /// </summary>
    public class EntityNameMap
    {
        private readonly Dictionary<(string Type, int Id), (string Name, string Url, int? CompanyId)> _map = new();
        public void Add(string type, int id, string name, string url, int? companyId)
            => _map[(type, id)] = (name, url, companyId);
        public string? GetName(string type, int id)
            => _map.TryGetValue((type, id), out var v) ? v.Name : null;
        public string? GetUrl(string type, int id)
            => _map.TryGetValue((type, id), out var v) ? v.Url : null;
        public int? GetCompanyId(string type, int id)
            => _map.TryGetValue((type, id), out var v) ? v.CompanyId : null;
    }

    private string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }
}
