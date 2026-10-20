using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using RentCollection.Infrastructure.Data;
using RentCollection.Core.Entities;

namespace RentCollection.Web.Controllers;

// SECURITY: the class-level [AllowAnonymous] is required — the global
// AuthorizeFilter in Program.cs demands the back-office cookie identity, which
// tenants never have (they authenticate via session). But that same marker
// disabled TenantAuthorizationFilter for EVERY action (it treats any
// IAllowAnonymous in the endpoint metadata as an opt-out), so actions that
// forgot their own session check were reachable by anyone. [RequireTenantSession]
// below closes that gap: it enforces the tenant session for every action and
// honours only a METHOD-level [AllowAnonymous] as the explicit opt-out.
[AllowAnonymous]
[RequireTenantSession]
public class TenantPortalController : Controller
{
    private const string RememberMeCookieName = "TenantRememberMe";
    private const string RememberMeProtectorPurpose = "TenantPortal.RememberMe.v1";

    private readonly ApplicationDbContext _context;
    private readonly IDataProtector _rememberMeProtector;

    public TenantPortalController(ApplicationDbContext context, IDataProtectionProvider dataProtectionProvider)
    {
        _context = context;
        _rememberMeProtector = dataProtectionProvider.CreateProtector(RememberMeProtectorPurpose);
    }

    // GET: TenantPortal/Login
    [AllowAnonymous]
    public async Task<IActionResult> Login()
    {
        // If already logged in via session, redirect to dashboard
        if (HttpContext.Session.GetInt32("TenantId").HasValue)
        {
            return RedirectToAction(nameof(Dashboard));
        }

        // Check for "Remember Me" cookie. The payload is a DataProtection-signed
        // tenantId — an attacker can't forge or rewrite it without the app key ring.
        if (Request.Cookies.TryGetValue(RememberMeCookieName, out string? protectedValue)
            && !string.IsNullOrEmpty(protectedValue))
        {
            int? tenantId = null;
            try
            {
                var unprotected = _rememberMeProtector.Unprotect(protectedValue);
                if (int.TryParse(unprotected, out var parsed))
                    tenantId = parsed;
            }
            catch
            {
                // Tampered / older key ring — fall through to delete below.
            }

            if (tenantId.HasValue)
            {
                var tenant = await _context.Tenants
                    .FirstOrDefaultAsync(t => t.TenantId == tenantId.Value && t.Status == "Active");

                if (tenant != null)
                {
                    HttpContext.Session.SetInt32("TenantId", tenant.TenantId);
                    HttpContext.Session.SetString("TenantName", $"{tenant.FirstName} {tenant.LastName}");
                    return RedirectToAction(nameof(Dashboard));
                }
            }

            Response.Cookies.Delete(RememberMeCookieName);
        }

        return View();
    }

    // POST: TenantPortal/Login
    [HttpPost]
    [ValidateAntiForgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> Login(string civilId, string password, bool rememberMe = false)
    {
        if (string.IsNullOrWhiteSpace(civilId))
        {
            TempData["ErrorMessage"] = "Please enter your Civil ID";
            return View();
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            TempData["ErrorMessage"] = "Please enter your password";
            return View();
        }

        var tenant = await _context.Tenants
            .FirstOrDefaultAsync(t => t.GovernmentId == civilId && t.Status == "Active");

        if (tenant == null)
        {
            TempData["ErrorMessage"] = "Invalid Civil ID or password. Please try again.";
            return View();
        }

        // Verify password using BCrypt
        if (string.IsNullOrEmpty(tenant.Password) || !BCrypt.Net.BCrypt.Verify(password, tenant.Password))
        {
            TempData["ErrorMessage"] = "Invalid Civil ID or password. Please try again.";
            return View();
        }

        // Store tenant ID in session
        HttpContext.Session.SetInt32("TenantId", tenant.TenantId);
        HttpContext.Session.SetString("TenantName", $"{tenant.FirstName} {tenant.LastName}");

        // Handle "Remember Me" with a DataProtection-signed cookie. Plaintext
        // TenantId would be trivial to forge; the protected payload is bound to
        // the app's data-protection key ring.
        if (rememberMe)
        {
            var cookieOptions = new Microsoft.AspNetCore.Http.CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(30),
                HttpOnly = true,
                Secure = true,
                SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax
            };

            var protectedValue = _rememberMeProtector.Protect(tenant.TenantId.ToString());
            Response.Cookies.Append(RememberMeCookieName, protectedValue, cookieOptions);
        }
        else
        {
            Response.Cookies.Delete(RememberMeCookieName);
        }

        TempData["SuccessMessage"] = $"Welcome, {tenant.FirstName}!";
        return RedirectToAction(nameof(Dashboard));
    }

    // GET: TenantPortal/Dashboard
    public async Task<IActionResult> Dashboard()
    {
        var tenantId = HttpContext.Session.GetInt32("TenantId");
        if (!tenantId.HasValue)
        {
            return RedirectToAction(nameof(Login));
        }

        var tenant = await _context.Tenants
            .Include(t => t.Company)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId.Value);

        if (tenant == null)
        {
            HttpContext.Session.Clear();
            return RedirectToAction(nameof(Login));
        }

        // Get all active leases for this tenant
        var leases = await _context.Leases
            .Include(l => l.Unit)
                .ThenInclude(u => u.Property)
            .Include(l => l.Tenant)
            .Where(l => l.TenantId == tenantId.Value && l.Status == "Active")
            .OrderByDescending(l => l.LeaseStartDate)
            .ToListAsync();

        // Get recent payments (the dashboard shows the last four; PaymentHistory has the rest)
        var payments = await _context.Payments
            .Include(p => p.Lease)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .Where(p => p.Lease.TenantId == tenantId.Value)
            .OrderByDescending(p => p.PaymentDate)
            .Take(4)
            .ToListAsync();

        // Account summary — what the tenant owes, when it's due, and which
        // invoice "Pay now" should open. Same status set and oldest-first order
        // as MakePayment / PaymentEligibilityService (pay-oldest-first rule).
        var today = DateTime.UtcNow.Date;
        var outstandingInvoices = await _context.Invoices
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId.Value &&
                   (i.Status == "Pending" || i.Status == "Overdue" || i.Status == "Partially Paid"))
            .OrderBy(i => i.InvoiceDate)
            .ThenBy(i => i.InvoiceId)
            .ToListAsync();

        var outstandingTotal = outstandingInvoices.Sum(i => Math.Max(0m, i.TotalAmount - i.PaidAmount));
        var overdueCount = outstandingInvoices.Count(i => i.Status == "Overdue" || i.DueDate.Date < today);
        var nextDueDate = outstandingInvoices.Count > 0 ? outstandingInvoices.Min(i => i.DueDate).Date : (DateTime?)null;
        var oldestUnpaidInvoice = outstandingInvoices.FirstOrDefault();

        // Lease-expiry nudge: soonest active lease ending within the next 30 days.
        var expiringLease = leases
            .Where(l => l.LeaseEndDate.Date >= today && (l.LeaseEndDate.Date - today).TotalDays <= 30)
            .OrderBy(l => l.LeaseEndDate)
            .FirstOrDefault();

        // Get pending maintenance requests
        var maintenanceRequests = await _context.MaintenanceRequests
            .Include(m => m.Property)
            .Include(m => m.Unit)
            .Where(m => m.TenantId == tenantId.Value && m.Status != "Completed" && m.Status != "Cancelled")
            .OrderByDescending(m => m.RequestDate)
            .ToListAsync();

        // Get lease-related documents for each lease
        var leaseIds = leases.Select(l => l.LeaseId).ToList();
        var documents = await _context.Documents
            .Where(d => d.RelatedEntityType == "Lease" && d.RelatedEntityId != null && leaseIds.Contains(d.RelatedEntityId.Value))
            .OrderByDescending(d => d.UploadedDate)
            .ToListAsync();

        ViewBag.Tenant = tenant;
        ViewBag.Leases = leases;
        ViewBag.Payments = payments;
        ViewBag.MaintenanceRequests = maintenanceRequests;
        ViewBag.Documents = documents;

        // Account summary + nudge (see above)
        ViewBag.OutstandingTotal = outstandingTotal;
        ViewBag.OutstandingCount = outstandingInvoices.Count;
        ViewBag.OverdueCount = overdueCount;
        ViewBag.DaysUntilNextDue = nextDueDate.HasValue ? (int?)(nextDueDate.Value - today).TotalDays : null;
        ViewBag.OldestUnpaidLeaseId = oldestUnpaidInvoice?.LeaseId;
        ViewBag.TotalMonthlyRent = leases.Sum(l => l.MonthlyRent);
        ViewBag.ExpiringLease = expiringLease;
        ViewBag.ExpiringLeaseDays = expiringLease != null ? (int?)(expiringLease.LeaseEndDate.Date - today).TotalDays : null;

        return View();
    }

    // GET: TenantPortal/MyDetails
    public async Task<IActionResult> MyDetails()
    {
        var tenantId = HttpContext.Session.GetInt32("TenantId");
        if (!tenantId.HasValue)
        {
            return RedirectToAction(nameof(Login));
        }

        var tenant = await _context.Tenants
            .Include(t => t.Company)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId.Value);

        if (tenant == null)
        {
            HttpContext.Session.Clear();
            return RedirectToAction(nameof(Login));
        }

        return View(tenant);
    }

    // GET: TenantPortal/MakePayment
    public async Task<IActionResult> MakePayment(int? leaseId)
    {
        var tenantId = HttpContext.Session.GetInt32("TenantId");
        if (!tenantId.HasValue)
        {
            return RedirectToAction(nameof(Login));
        }

        // Sprint 24 — include "Partially Paid" so the view's oldest-first
        // gate sees the full queue. Order by InvoiceDate to match
        // PaymentEligibilityService (server-authoritative).
        var invoices = await _context.Invoices
            .Include(i => i.Lease)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .Where(i => i.TenantId == tenantId.Value &&
                   (i.Status == "Pending" || i.Status == "Overdue" || i.Status == "Partially Paid"))
            .OrderBy(i => i.InvoiceDate)
            .ThenBy(i => i.InvoiceId)
            .ToListAsync();

        // Get properties for payment method configuration
        var propertyIds = invoices.Select(i => i.Lease.Unit.PropertyId).Distinct().ToList();
        var properties = await _context.Properties
            .Where(p => propertyIds.Contains(p.PropertyId))
            .ToListAsync();

        ViewBag.Invoices = invoices;
        ViewBag.Properties = properties;

        return View();
    }

    // Sprint 24 — removed legacy POST: TenantPortal/SubmitPayment. The
    // action wrote directly to Payments with no invoice link, no
    // eligibility check, no idempotency key. All live payment flows go
    // through /api/payments/knet/initiate or /api/payments/bank/upload,
    // which enforce the oldest-unpaid-first rule via IPaymentEligibilityService.
    // No view or client posts to this URL — confirmed by repo grep before removal.

    // GET: TenantPortal/ViewReceipt
    public async Task<IActionResult> ViewReceipt(int paymentId)
    {
        var tenantId = HttpContext.Session.GetInt32("TenantId");
        if (!tenantId.HasValue)
        {
            return RedirectToAction(nameof(Login));
        }

        var payment = await _context.Payments
            .Include(p => p.Lease)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .Include(p => p.Lease)
                .ThenInclude(l => l.Tenant)
                    .ThenInclude(t => t.Company)
            .FirstOrDefaultAsync(p => p.PaymentId == paymentId && p.Lease.TenantId == tenantId.Value);

        if (payment == null)
        {
            TempData["ErrorMessage"] = "Payment receipt not found";
            return RedirectToAction(nameof(Dashboard));
        }

        ViewBag.AmountInWords = ConvertAmountToWords(payment.AmountPaid);
        return View(payment);
    }

    // GET: TenantPortal/PaymentHistory
    public async Task<IActionResult> PaymentHistory()
    {
        var tenantId = HttpContext.Session.GetInt32("TenantId");
        if (!tenantId.HasValue)
        {
            return RedirectToAction(nameof(Login));
        }

        var payments = await _context.Payments
            .Include(p => p.Lease)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .Where(p => p.Lease.TenantId == tenantId.Value)
            .OrderByDescending(p => p.PaymentDate)
            .ToListAsync();

        return View(payments);
    }

    // POST: TenantPortal/CreateMaintenanceRequest
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateMaintenanceRequest(int leaseId, string category, string priority, string description)
    {
        var tenantId = HttpContext.Session.GetInt32("TenantId");
        if (!tenantId.HasValue)
        {
            return RedirectToAction(nameof(Login));
        }

        // Validate inputs
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(priority) || string.IsNullOrWhiteSpace(description))
        {
            TempData["ErrorMessage"] = "Please fill in all required fields";
            return RedirectToAction(nameof(Dashboard));
        }

        // Get the lease to extract property and unit information
        var lease = await _context.Leases
            .Include(l => l.Unit)
                .ThenInclude(u => u.Property)
            .FirstOrDefaultAsync(l => l.LeaseId == leaseId && l.TenantId == tenantId.Value);

        if (lease == null)
        {
            TempData["ErrorMessage"] = "Invalid lease selected";
            return RedirectToAction(nameof(Dashboard));
        }

        // Create the maintenance request
        var maintenanceRequest = new MaintenanceRequest
        {
            PropertyId = lease.Unit.PropertyId,
            UnitId = lease.UnitId,
            TenantId = tenantId.Value,
            Category = category,
            Priority = priority,
            Description = description,
            Status = "Open",
            RequestDate = DateTime.UtcNow,
            CreatedDate = DateTime.UtcNow
        };

        _context.MaintenanceRequests.Add(maintenanceRequest);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Maintenance request submitted successfully! We will review it shortly.";
        return RedirectToAction(nameof(Dashboard));
    }

    // GET: TenantPortal/MakePaymentSimple
    public async Task<IActionResult> MakePaymentSimple()
    {
        var tenantId = HttpContext.Session.GetInt32("TenantId");
        if (!tenantId.HasValue)
        {
            return RedirectToAction(nameof(Login));
        }

        // Sprint 24 — include "Partially Paid" so the view's oldest-first
        // gate sees the full queue. Order by InvoiceDate to match
        // PaymentEligibilityService (server-authoritative).
        var invoices = await _context.Invoices
            .Include(i => i.Lease)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .Where(i => i.TenantId == tenantId.Value &&
                   (i.Status == "Pending" || i.Status == "Overdue" || i.Status == "Partially Paid"))
            .OrderBy(i => i.InvoiceDate)
            .ThenBy(i => i.InvoiceId)
            .ToListAsync();

        ViewBag.Invoices = invoices;

        return View();
    }

    // GET: TenantPortal/TestBankTransfer
    public IActionResult TestBankTransfer()
    {
        return View();
    }

    // SECURITY: removed the anonymous TestPayment card-simulation page. It
    // rendered gateway-supplied query values with no session and existed only
    // for KNET test mode.

    // GET: TenantPortal/PaymentSuccess
    // Anonymous because the KNET gateway redirects the payer's browser here,
    // which may not carry a tenant session (mobile app webview). Payment
    // details are shown ONLY when the session tenant owns the payment —
    // previously any paymentId was loaded with its Property (KNET credentials).
    [AllowAnonymous]
    public async Task<IActionResult> PaymentSuccess(string? paymentId, string? reference)
    {
        ViewBag.PaymentId = paymentId;
        ViewBag.Reference = reference;

        var tenantId = HttpContext.Session.GetInt32("TenantId");
        if (tenantId.HasValue && !string.IsNullOrEmpty(paymentId) && int.TryParse(paymentId, out int pId))
        {
            // No Includes — the view only needs AmountPaid/PaymentDate, and the
            // Property graph carries gateway credentials.
            var payment = await _context.Payments
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PaymentId == pId && p.TenantId == tenantId.Value);

            if (payment != null)
            {
                ViewBag.Payment = payment;
            }
        }

        return View();
    }

    // GET: TenantPortal/PaymentFailed
    [AllowAnonymous]
    public IActionResult PaymentFailed(string? error, string? reference)
    {
        ViewBag.Error = error ?? "Payment was not completed";
        ViewBag.Reference = reference;
        return View();
    }

    // GET: TenantPortal/Logout
    // Anonymous so an expired session can still clear its remember-me cookie.
    [AllowAnonymous]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();

        // Remove the remember me cookie on logout
        Response.Cookies.Delete(RememberMeCookieName);

        TempData["InfoMessage"] = "You have been logged out successfully";
        return RedirectToAction(nameof(Login));
    }

    // Helper method to convert number to words
    private string ConvertAmountToWords(decimal amount)
    {
        if (amount == 0)
            return "Zero";

        var dinars = (int)Math.Floor(amount);
        var fils = (int)Math.Round((amount - dinars) * 1000);

        string words = ConvertNumberToWords(dinars) + " Dinar";
        if (dinars != 1)
            words += "s";

        if (fils > 0)
        {
            words += " and " + ConvertNumberToWords(fils) + " Fil";
            if (fils != 1)
                words += "s";
        }

        return words;
    }

    private string ConvertNumberToWords(int number)
    {
        if (number == 0)
            return "Zero";

        if (number < 0)
            return "Minus " + ConvertNumberToWords(Math.Abs(number));

        string words = "";

        if ((number / 1000000) > 0)
        {
            words += ConvertNumberToWords(number / 1000000) + " Million ";
            number %= 1000000;
        }

        if ((number / 1000) > 0)
        {
            words += ConvertNumberToWords(number / 1000) + " Thousand ";
            number %= 1000;
        }

        if ((number / 100) > 0)
        {
            words += ConvertNumberToWords(number / 100) + " Hundred ";
            number %= 100;
        }

        if (number > 0)
        {
            if (words != "")
                words += "and ";

            var unitsMap = new[] { "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen" };
            var tensMap = new[] { "Zero", "Ten", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety" };

            if (number < 20)
                words += unitsMap[number];
            else
            {
                words += tensMap[number / 10];
                if ((number % 10) > 0)
                    words += "-" + unitsMap[number % 10];
            }
        }

        return words.Trim();
    }

    // SECURITY: the anonymous GenerateHash (reflected XSS, default-password
    // advertisement) and DebugTenants (listed Civil IDs) debug endpoints were
    // removed. Tenant credentials are issued via ITenantCredentialService.
}

/// <summary>
/// Requires a tenant session (<c>Session["TenantId"]</c>) for every action on the
/// controller it decorates, except actions that carry their own method-level
/// <see cref="AllowAnonymousAttribute"/>. Unlike <c>TenantAuthorizationFilter</c>,
/// a class-level [AllowAnonymous] does NOT opt the whole controller out — that
/// marker is only there to bypass the back-office cookie policy.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RequireTenantSessionAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is ControllerActionDescriptor action
            && action.MethodInfo.IsDefined(typeof(AllowAnonymousAttribute), inherit: true))
        {
            return; // Explicitly public action (login, logout, gateway return pages)
        }

        if (!context.HttpContext.Session.GetInt32("TenantId").HasValue)
        {
            context.Result = new RedirectToActionResult("Login", "TenantPortal", null);
        }
    }
}
