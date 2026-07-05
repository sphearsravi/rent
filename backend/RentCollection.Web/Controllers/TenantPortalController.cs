using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using RentCollection.Infrastructure.Data;
using RentCollection.Core.Entities;
using RentCollection.Web.Filters;

namespace RentCollection.Web.Controllers;

[AllowAnonymous]
[ServiceFilter(typeof(TenantAuthorizationFilter))]
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

        // Get recent payments
        var payments = await _context.Payments
            .Include(p => p.Lease)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .Where(p => p.Lease.TenantId == tenantId.Value)
            .OrderByDescending(p => p.PaymentDate)
            .Take(10)
            .ToListAsync();

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

    // GET: TenantPortal/TestPayment (Test mode payment simulation page)
    [AllowAnonymous]
    public IActionResult TestPayment(int paymentId, string knetPaymentId, decimal amount, int invoiceId)
    {
        ViewBag.PaymentId = paymentId;
        ViewBag.KnetPaymentId = knetPaymentId;
        ViewBag.Amount = amount;
        ViewBag.InvoiceId = invoiceId;
        return View();
    }

    // GET: TenantPortal/PaymentSuccess
    [AllowAnonymous]
    public async Task<IActionResult> PaymentSuccess(string? paymentId, string? reference)
    {
        ViewBag.PaymentId = paymentId;
        ViewBag.Reference = reference;

        // Get payment details if available
        if (!string.IsNullOrEmpty(paymentId) && int.TryParse(paymentId, out int pId))
        {
            var payment = await _context.Payments
                .Include(p => p.Invoice)
                .Include(p => p.Property)
                .FirstOrDefaultAsync(p => p.PaymentId == pId);

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

    // TEMPORARY: Generate BCrypt hash for password - REMOVE IN PRODUCTION
    [HttpGet]
    public IActionResult GenerateHash(string password = "Welcome@123")
    {
        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        var verify = BCrypt.Net.BCrypt.Verify(password, hash);

        return Content($@"
            <html>
            <body style='font-family: monospace; padding: 20px;'>
                <h2>BCrypt Hash Generator</h2>
                <p><strong>Password:</strong> {password}</p>
                <p><strong>Hash:</strong> {hash}</p>
                <p><strong>Verification:</strong> {verify}</p>
                <hr>
                <p>SQL to update all tenants:</p>
                <textarea style='width: 100%; height: 150px; font-family: monospace;'>
UPDATE Tenants SET Password = '{hash}' WHERE Password IS NULL OR Password = '$2a$11$vWZQV5Y5rGxK5J5K5J5K5.J5K5J5K5J5K5J5K5J5K5J5K5J5K5Jm';
                </textarea>
                <hr>
                <p><a href='/TenantPortal/Login'>Back to Login</a></p>
            </body>
            </html>
        ", "text/html");
    }

    // TEMPORARY: Debug endpoint to see tenant data - REMOVE IN PRODUCTION
    [HttpGet]
    public async Task<IActionResult> DebugTenants()
    {
        var tenants = await _context.Tenants
            .Where(t => t.Status == "Active")
            .Select(t => new
            {
                t.TenantId,
                t.FirstName,
                t.LastName,
                t.PACINumber,
                t.GovernmentId,
                HasPassword = t.Password != null ? "Yes" : "No",
                t.Status
            })
            .Take(10)
            .ToListAsync();

        var html = @"
            <html>
            <head>
                <style>
                    body { font-family: monospace; padding: 20px; }
                    table { border-collapse: collapse; width: 100%; }
                    th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }
                    th { background-color: #4CAF50; color: white; }
                    tr:nth-child(even) { background-color: #f2f2f2; }
                </style>
            </head>
            <body>
                <h2>Active Tenants Debug Info</h2>
                <table>
                    <tr>
                        <th>ID</th>
                        <th>Name</th>
                        <th>PACI Number</th>
                        <th>Government ID</th>
                        <th>Has Password</th>
                        <th>Status</th>
                    </tr>";

        foreach (var tenant in tenants)
        {
            html += $@"
                    <tr>
                        <td>{tenant.TenantId}</td>
                        <td>{tenant.FirstName} {tenant.LastName}</td>
                        <td>{tenant.PACINumber ?? "<em>NULL</em>"}</td>
                        <td>{tenant.GovernmentId ?? "<em>NULL</em>"}</td>
                        <td>{tenant.HasPassword}</td>
                        <td>{tenant.Status}</td>
                    </tr>";
        }

        html += @"
                </table>
                <hr>
                <p><strong>Instructions:</strong></p>
                <ol>
                    <li>Use the value from either 'PACI Number' or 'Government ID' column (whichever has data) as your Civil ID</li>
                    <li>Visit <a href='/TenantPortal/GenerateHash'>Generate Hash</a> to get the SQL to set passwords</li>
                    <li>After setting passwords, try logging in with the Civil ID and password 'Welcome@123'</li>
                </ol>
                <p><a href='/TenantPortal/Login'>Back to Login</a></p>
            </body>
            </html>";

        return Content(html, "text/html");
    }
}
