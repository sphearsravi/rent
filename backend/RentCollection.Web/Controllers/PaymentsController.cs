using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.Controllers;

[Authorize(Roles = Roles.Financial)]
public class PaymentsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IPaymentVerificationService _verificationService;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        ApplicationDbContext context,
        IPaymentVerificationService verificationService,
        ILogger<PaymentsController> logger)
    {
        _context = context;
        _verificationService = verificationService;
        _logger = logger;
    }

    // Company scope: Payment has no company query filter and the verification
    // service is unscoped. Guard by-id lookups via the owning Tenant and
    // post-filter lists to the tenants visible through the filtered Tenants
    // set. SuperAdmin is cross-company by design.
    private bool CanAccessCompany(int? entityCompanyId)
    {
        if (User.IsInRole(Roles.SuperAdmin)) return true;
        return int.TryParse(User.FindFirst("companyId")?.Value, out var cid)
            && entityCompanyId == cid;
    }

    private async Task<HashSet<int>?> VisibleTenantIdsAsync()
    {
        if (User.IsInRole(Roles.SuperAdmin)) return null;   // null = no filter
        return (await _context.Tenants.Select(t => t.TenantId).ToListAsync()).ToHashSet();
    }

    /// <summary>
    /// Inclusive end-of-day for a user-picked end date: "to 12 Oct" means up
    /// to 23:59:59.9999999 on 12 Oct, not midnight at its start.
    /// </summary>
    private static DateTime EndOfDay(DateTime d) => d.Date.AddDays(1).AddTicks(-1);

    public async Task<IActionResult> Index(
        string? civilId = null,
        int? leaseId = null,
        int? propertyId = null,
        string? knetMid = null,
        bool? hasMismatch = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? status = null)
    {
        try
        {
            // Build filter from query parameters
            var filter = new PaymentVerificationFilterDto
            {
                CivilID = civilId,
                LeaseId = leaseId,
                PropertyId = propertyId,
                KNETMID = knetMid,
                HasMismatch = hasMismatch,
                StartDate = startDate?.Date,
                // The service compares PaymentDate <= EndDate; pass end-of-day
                // so the chosen end date is inclusive.
                EndDate = endDate.HasValue ? EndOfDay(endDate.Value) : null,
                Status = status
            };

            var payments = await _verificationService.GetPaymentsForVerificationAsync(filter);

            // Company scope — the service is unscoped.
            var visibleTenants = await VisibleTenantIdsAsync();
            if (visibleTenants != null)
                payments = payments.Where(p => visibleTenants.Contains(p.TenantId)).ToList();

            // Pass filter values to view for form persistence
            ViewBag.CivilId = civilId;
            ViewBag.LeaseId = leaseId;
            ViewBag.PropertyId = propertyId;
            ViewBag.KnetMid = knetMid;
            ViewBag.HasMismatch = hasMismatch;
            ViewBag.StartDate = startDate;
            ViewBag.EndDate = endDate;
            ViewBag.Status = status;

            // Get properties for filter dropdown
            ViewBag.Properties = await _context.Properties
                .Select(p => new { p.PropertyId, p.PropertyName, p.KNETMID })
                .OrderBy(p => p.PropertyName)
                .ToListAsync();

            // Get mismatch count for badge
            ViewBag.MismatchCount = payments.Count(p => p.HasMismatch);

            // 2026-10 list redesign — KPI strip over ALL of the company's
            // payments (not just the filtered page): collected this month,
            // awaiting verification (cash/bank), failed in the last 30 days.
            var scoped = _context.Payments.AsNoTracking().AsQueryable();
            if (visibleTenants != null) scoped = scoped.Where(p => visibleTenants.Contains(p.TenantId));
            var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var since30 = DateTime.Today.AddDays(-30);
            var completed = scoped.Where(p => p.Status == RentCollection.Core.Enums.PaymentStatus.Completed);
            ViewBag.KpiCollectedMtd = await completed.Where(p => p.PaymentDate >= monthStart).SumAsync(p => (decimal?)p.AmountPaid) ?? 0m;
            ViewBag.KpiCollectedMtdCount = await completed.CountAsync(p => p.PaymentDate >= monthStart);
            ViewBag.KpiCollectedAll = await completed.SumAsync(p => (decimal?)p.AmountPaid) ?? 0m;
            var pendingQ = scoped.Where(p => p.Status == RentCollection.Core.Enums.PaymentStatus.PendingVerification);
            ViewBag.KpiPendingCount = await pendingQ.CountAsync();
            ViewBag.KpiPendingAmount = await pendingQ.SumAsync(p => (decimal?)p.AmountPaid) ?? 0m;
            ViewBag.KpiFailed30 = await scoped.CountAsync(p => p.Status == RentCollection.Core.Enums.PaymentStatus.Failed && p.PaymentDate >= since30);
            ViewBag.KpiMismatchAll = await scoped.CountAsync(p => p.HasMismatch);

            return View(payments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading payments for verification");
            TempData["ErrorMessage"] = "An error occurred while loading payments. Please try again.";
            return View(new List<RentCollection.Application.DTOs.AdminPaymentVerificationDto>());
        }
    }

    /// <summary>
    /// Shows only payments with mismatch flags
    /// </summary>
    public async Task<IActionResult> Mismatches()
    {
        try
        {
            var mismatchedPayments = await _verificationService.GetMismatchedPaymentsAsync();

            // Company scope — the service is unscoped.
            var visibleTenants = await VisibleTenantIdsAsync();
            if (visibleTenants != null)
                mismatchedPayments = mismatchedPayments.Where(p => visibleTenants.Contains(p.TenantId)).ToList();

            return View("Index", mismatchedPayments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading mismatched payments");
            TempData["ErrorMessage"] = "An error occurred while loading mismatched payments.";
            return View("Index", new List<RentCollection.Application.DTOs.AdminPaymentVerificationDto>());
        }
    }

    /// <summary>
    /// Shows payment details with verification information
    /// </summary>
    public async Task<IActionResult> Details(int id)
    {
        try
        {
            // Resolve the payment by id first (the service has no by-id lookup
            // and used to load EVERY payment to find one), and enforce company
            // scope via the owning Tenant before building the verification DTO.
            var target = await _context.Payments
                .AsNoTracking()
                .Where(p => p.PaymentId == id)
                .Select(p => new { p.PaymentId, p.LeaseId, p.PaymentDate, CompanyId = (int?)p.Tenant.CompanyId })
                .FirstOrDefaultAsync();
            if (target == null || !CanAccessCompany(target.CompanyId))
            {
                return NotFound();
            }

            // Narrow the service query to this payment's lease + date so it
            // returns a handful of rows instead of the whole table.
            var filter = new PaymentVerificationFilterDto
            {
                LeaseId = target.LeaseId,
                StartDate = target.PaymentDate,
                EndDate = target.PaymentDate,
            };
            var payments = await _verificationService.GetPaymentsForVerificationAsync(filter);
            var payment = payments.FirstOrDefault(p => p.PaymentId == id);

            if (payment == null)
            {
                return NotFound();
            }

            // Get audit history
            var auditHistory = await _verificationService.GetPaymentAuditHistoryAsync(id);
            ViewBag.AuditHistory = auditHistory;

            return View(payment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading payment details for PaymentId: {PaymentId}", id);
            TempData["ErrorMessage"] = "An error occurred while loading payment details.";
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>
    /// Resolves a payment mismatch
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveMismatch(int paymentId, string? resolutionNotes)
    {
        try
        {
            var userName = User.Identity?.Name ?? "Unknown";
            var success = await _verificationService.ResolveMismatchAsync(paymentId, userName, resolutionNotes);

            if (success)
            {
                TempData["SuccessMessage"] = "Mismatch resolved successfully.";
            }
            else
            {
                TempData["ErrorMessage"] = "Failed to resolve mismatch.";
            }

            return RedirectToAction(nameof(Details), new { id = paymentId });
        }
        catch (Exception)
        {
            TempData["ErrorMessage"] = "An error occurred while resolving the mismatch.";
            return RedirectToAction(nameof(Details), new { id = paymentId });
        }
    }

    /// <summary>
    /// Shows reconciliation summary
    /// </summary>
    public async Task<IActionResult> Reconciliation(
        DateTime? startDate = null,
        DateTime? endDate = null,
        int? propertyId = null)
    {
        try
        {
            // Default to current month if no dates provided. A user-picked end
            // date is made inclusive (end of that day) — the service compares
            // PaymentDate <= endDate.
            var start = startDate?.Date ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var end = endDate.HasValue ? EndOfDay(endDate.Value) : DateTime.Now;

            var summary = await _verificationService.GetReconciliationSummaryAsync(start, end, propertyId);

            ViewBag.StartDate = start;
            ViewBag.EndDate = endDate ?? end;
            ViewBag.PropertyId = propertyId;

            // Get properties for filter dropdown
            ViewBag.Properties = await _context.Properties
                .Select(p => new { p.PropertyId, p.PropertyName })
                .OrderBy(p => p.PropertyName)
                .ToListAsync();

            return View(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading reconciliation summary");
            TempData["ErrorMessage"] = "An error occurred while loading the reconciliation summary.";
            return View(new RentCollection.Application.Interfaces.ReconciliationSummaryDto());
        }
    }

    /// <summary>
    /// Shows KNET reconciliation data
    /// </summary>
    public async Task<IActionResult> KNETReconciliation(
        DateTime? startDate = null,
        DateTime? endDate = null,
        int? propertyId = null)
    {
        try
        {
            // Inclusive end date — see Reconciliation.
            var start = startDate?.Date ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var end = endDate.HasValue ? EndOfDay(endDate.Value) : DateTime.Now;

            var data = await _verificationService.GetKNETReconciliationDataAsync(start, end, propertyId);

            ViewBag.StartDate = start;
            ViewBag.EndDate = endDate ?? end;
            ViewBag.PropertyId = propertyId;

            // Get properties for filter dropdown
            ViewBag.Properties = await _context.Properties
                .Select(p => new { p.PropertyId, p.PropertyName, p.KNETAccountID, p.KNETMID })
                .OrderBy(p => p.PropertyName)
                .ToListAsync();

            return View(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading KNET reconciliation data");
            TempData["ErrorMessage"] = "An error occurred while loading KNET reconciliation data.";
            return View(new List<RentCollection.Application.Interfaces.KNETReconciliationDto>());
        }
    }
}
