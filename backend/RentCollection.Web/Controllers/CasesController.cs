using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.Controllers;

/// <summary>
/// Legal / court cases filed on a lease. Lightweight "track + badge" scope:
/// cases are created and edited from the lease's Details page (no separate
/// list module). A case is always attached to a Lease; TenantId + CompanyId
/// are derived from that lease so the tenant app and company-scope filter work.
/// </summary>
[Authorize(Roles = Roles.Operational)]
public class CasesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IAuditLogService _audit;
    private readonly ILogger<CasesController> _logger;

    public CasesController(ApplicationDbContext context, IAuditLogService audit,
        ILogger<CasesController> logger)
    {
        _context = context;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>
    /// Create or update the legal case on a lease. LegalCaseId == 0 → create.
    /// Posts from the "Legal case" panel on /Leases/Details/{leaseId}.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        int legalCaseId, int leaseId,
        string? caseNumber, string caseType, string status,
        DateTime filedDate, DateTime? nextHearingDate,
        decimal? amountClaimed, string? notes)
    {
        // The lease anchors the case and supplies TenantId + CompanyId. Loading it
        // through the (company-scoped) filter also enforces that the operator may
        // touch this lease at all.
        var lease = await _context.Leases
            .Include(l => l.Tenant)
            .FirstOrDefaultAsync(l => l.LeaseId == leaseId);
        if (lease == null)
            return NotFound();

        // Guard against unknown enum values from a tampered form.
        if (!LegalCaseTypes.All.Contains(caseType)) caseType = LegalCaseTypes.Other;
        if (!LegalCaseStatuses.All.Contains(status)) status = LegalCaseStatuses.Open;

        var who = User.Identity?.Name ?? "system";
        var isNew = legalCaseId == 0;

        LegalCase legalCase;
        if (isNew)
        {
            legalCase = new LegalCase
            {
                LeaseId = lease.LeaseId,
                TenantId = lease.TenantId,
                CompanyId = lease.Tenant?.CompanyId ?? 0,
                CreatedDate = DateTime.UtcNow,
                CreatedBy = who,
            };
            _context.LegalCases.Add(legalCase);
        }
        else
        {
            legalCase = await _context.LegalCases
                .FirstOrDefaultAsync(c => c.LegalCaseId == legalCaseId && c.LeaseId == leaseId);
            if (legalCase == null)
                return NotFound();
            legalCase.ModifiedDate = DateTime.UtcNow;
            legalCase.ModifiedBy = who;
        }

        legalCase.CaseNumber = string.IsNullOrWhiteSpace(caseNumber) ? null : caseNumber.Trim();
        legalCase.CaseType = caseType;
        legalCase.Status = status;
        legalCase.FiledDate = filedDate == default ? DateTime.UtcNow : filedDate;
        legalCase.NextHearingDate = nextHearingDate;
        legalCase.AmountClaimed = amountClaimed;
        legalCase.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        // First save assigns LegalCaseId (needed in the audit payload for a new
        // case); IAuditLogService.WriteAsync only Adds the row to the context, so
        // a second SaveChanges is required or the audit entry is silently lost.
        await _context.SaveChangesAsync();

        await _audit.WriteAsync("Lease", lease.LeaseId,
            isNew ? "LegalCaseFiled" : "LegalCaseUpdated", who,
            new { legalCase.LegalCaseId, legalCase.CaseNumber, legalCase.CaseType, legalCase.Status });
        await _context.SaveChangesAsync();

        TempData["CaseSaved"] = isNew ? "Legal case recorded." : "Legal case updated.";
        return RedirectToAction("Details", "Leases", new { id = leaseId });
    }

    /// <summary>
    /// Remove a case entirely (e.g. filed in error). Distinct from setting it to
    /// a closed status, which keeps the record.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int legalCaseId, int leaseId)
    {
        var legalCase = await _context.LegalCases
            .FirstOrDefaultAsync(c => c.LegalCaseId == legalCaseId && c.LeaseId == leaseId);
        if (legalCase != null)
        {
            _context.LegalCases.Remove(legalCase);
            // Audit row is added to the context BEFORE the save so it is persisted
            // in the same SaveChanges (it was previously written after and lost).
            await _audit.WriteAsync("Lease", leaseId, "LegalCaseRemoved",
                User.Identity?.Name ?? "system", new { legalCase.LegalCaseId });
            await _context.SaveChangesAsync();
            TempData["CaseSaved"] = "Legal case removed.";
        }
        return RedirectToAction("Details", "Leases", new { id = leaseId });
    }
}
