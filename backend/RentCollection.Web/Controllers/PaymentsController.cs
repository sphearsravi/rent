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
                StartDate = startDate,
                EndDate = endDate,
                Status = status
            };

            var payments = await _verificationService.GetPaymentsForVerificationAsync(filter);

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

            return View(payments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading payments for verification");
            TempData["Error"] = "An error occurred while loading payments. Please try again.";
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
            return View("Index", mismatchedPayments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading mismatched payments");
            TempData["Error"] = "An error occurred while loading mismatched payments.";
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
            var filter = new PaymentVerificationFilterDto();
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
            TempData["Error"] = "An error occurred while loading payment details.";
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
                TempData["Success"] = "Mismatch resolved successfully.";
            }
            else
            {
                TempData["Error"] = "Failed to resolve mismatch.";
            }

            return RedirectToAction(nameof(Details), new { id = paymentId });
        }
        catch (Exception)
        {
            TempData["Error"] = "An error occurred while resolving the mismatch.";
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
            // Default to current month if no dates provided
            var start = startDate ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var end = endDate ?? DateTime.Now;

            var summary = await _verificationService.GetReconciliationSummaryAsync(start, end, propertyId);

            ViewBag.StartDate = start;
            ViewBag.EndDate = end;
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
            TempData["Error"] = "An error occurred while loading the reconciliation summary.";
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
            var start = startDate ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var end = endDate ?? DateTime.Now;

            var data = await _verificationService.GetKNETReconciliationDataAsync(start, end, propertyId);

            ViewBag.StartDate = start;
            ViewBag.EndDate = end;
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
            TempData["Error"] = "An error occurred while loading KNET reconciliation data.";
            return View(new List<RentCollection.Application.Interfaces.KNETReconciliationDto>());
        }
    }
}
