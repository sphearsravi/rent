using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RentCollection.Application.Services;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;
using RentCollection.Web.Models.ViewModels;
using System.Security.Claims;

namespace RentCollection.Web.Controllers;

public class AccountController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AccountController> _logger;
    private readonly IPasswordHasher _passwordHasher;

    public AccountController(
        ApplicationDbContext context,
        ILogger<AccountController> logger)
    {
        _context = context;
        _logger = logger;
        _passwordHasher = new PasswordHasher();
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        ViewData["ReturnUrl"] = model.ReturnUrl;

        if (ModelState.IsValid)
        {
            // Try to find user in custom Users table by email or username
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == model.Email || u.Username == model.Email);

            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return View(model);
            }

            if (!user.IsActive)
            {
                ModelState.AddModelError(string.Empty, "Your account has been deactivated. Please contact administrator.");
                return View(model);
            }

            // Verify password using custom password hasher
            if (!_passwordHasher.VerifyPassword(model.Password, user.PasswordHash))
            {
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return View(model);
            }

            // Create claims for the user. The `companyId` claim is what
            // backs Item 2's company-scope EF query filter — without it,
            // logged-in users see no rows.
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.GivenName, user.FirstName),
                new Claim(ClaimTypes.Surname, user.LastName),
                new Claim(ClaimTypes.Role, user.Role)
            };
            if (user.CompanyId.HasValue)
            {
                claims.Add(new Claim("companyId", user.CompanyId.Value.ToString()));
            }

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,
                ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddHours(12),
                // Sprint 25 — Remember-me fix. The cookie auth options have
                // SlidingExpiration=true + ExpireTimeSpan=12h. With AllowRefresh
                // at its default (true), the cookie is re-issued on every
                // request with a fresh 12h ExpiresUtc — overwriting our
                // 30-day window above. Setting AllowRefresh=false for the
                // persistent path preserves the 30-day expiry across browser
                // restarts as the user expects. Non-persistent stays default
                // so a session cookie can still slide within its 12h window.
                AllowRefresh = !model.RememberMe,
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            // Update last login
            user.LastLogin = DateTime.UtcNow;
            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"User {user.Email} logged in successfully.");

            // Never honor a ReturnUrl that points back at the auth endpoints — a
            // stale ReturnUrl of /Account/Logout would sign the user straight back
            // out right after logging in (a login->logout loop).
            if (!string.IsNullOrEmpty(model.ReturnUrl)
                && Url.IsLocalUrl(model.ReturnUrl)
                && model.ReturnUrl.IndexOf("/Account/Logout", StringComparison.OrdinalIgnoreCase) < 0
                && model.ReturnUrl.IndexOf("/Account/Login", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return Redirect(model.ReturnUrl);
            }
            return RedirectToAction("Index", "Dashboard");
        }

        return View(model);
    }

    [HttpGet]
    [Authorize(Roles = Roles.AnyAdmin)]
    public IActionResult Register()
    {
        // Redirect to Users/Create instead
        return RedirectToAction("Create", "Users");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        // Sign out from cookie authentication
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        _logger.LogInformation("User logged out.");
        return RedirectToAction("Login", "Account");
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        return View();
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPassword()
    {
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public IActionResult ForgotPassword(string email)
    {
        // The reset flow is not implemented (no signed-token issuance, no email
        // delivery, no rate-limiting). Returning success here would mislead users
        // and let an attacker fingerprint accounts. Surface a clear message until
        // the feature is built.
        if (!string.IsNullOrEmpty(email))
            _logger.LogInformation("Password reset requested for {Email} — feature not implemented", email);

        ModelState.AddModelError("",
            "Password reset is temporarily unavailable. Please contact your administrator.");
        return View();
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPasswordConfirmation()
    {
        return View();
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ResetPassword(string? code = null)
    {
        if (code == null)
        {
            return BadRequest("A code must be supplied for password reset.");
        }

        var model = new ResetPasswordViewModel { Code = code };
        return View(model);
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public IActionResult ResetPassword(ResetPasswordViewModel model)
    {
        // Disabled until a signed-token reset flow is implemented (see ForgotPassword).
        // Returning a fake success would let an attacker harvest valid emails.
        _logger.LogWarning("Password reset POST attempted but feature is disabled");
        ModelState.AddModelError("", "Password reset is temporarily unavailable.");
        return View(model);
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ResetPasswordConfirmation()
    {
        return View();
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Profile()
    {
        var user = await CurrentUserAsync();
        if (user == null) return NotFound();
        return View(user);
    }

    /// <summary>
    /// Self-service profile update — name + phone only. Email is shown
    /// read-only because changing it touches the login identifier and
    /// belongs to a separate re-verification flow. Role + CompanyId are
    /// always immutable from this endpoint; only an Admin can change them
    /// via /Users/ChangeRole or /Users/Edit.
    /// </summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(string firstName, string lastName, string? phoneNumber)
    {
        var user = await CurrentUserAsync();
        if (user == null) return NotFound();

        user.FirstName = firstName?.Trim() ?? user.FirstName;
        user.LastName = lastName?.Trim() ?? user.LastName;
        user.PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        user.ModifiedDate = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Profile updated.";
        return RedirectToAction(nameof(Profile));
    }

    /// <summary>
    /// Self-service password change. Requires the current password to
    /// confirm identity even though the user is already authenticated —
    /// guards against an attacker who walked away from an unlocked
    /// session changing the credentials.
    /// </summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeOwnPassword(string currentPassword, string newPassword, string confirmNewPassword)
    {
        var user = await CurrentUserAsync();
        if (user == null) return NotFound();

        if (string.IsNullOrWhiteSpace(currentPassword)
            || string.IsNullOrWhiteSpace(newPassword)
            || string.IsNullOrWhiteSpace(confirmNewPassword))
        {
            TempData["ErrorMessage"] = "All password fields are required.";
            return RedirectToAction(nameof(Profile));
        }
        if (!string.Equals(newPassword, confirmNewPassword, StringComparison.Ordinal))
        {
            TempData["ErrorMessage"] = "New password and confirmation do not match.";
            return RedirectToAction(nameof(Profile));
        }
        if (newPassword.Length < 8)
        {
            TempData["ErrorMessage"] = "New password must be at least 8 characters long.";
            return RedirectToAction(nameof(Profile));
        }
        if (!_passwordHasher.VerifyPassword(currentPassword, user.PasswordHash))
        {
            TempData["ErrorMessage"] = "Current password is incorrect.";
            return RedirectToAction(nameof(Profile));
        }

        user.PasswordHash = _passwordHasher.HashPassword(newPassword);
        user.ModifiedDate = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Password changed. Use the new password on your next login.";
        return RedirectToAction(nameof(Profile));
    }

    private async Task<User?> CurrentUserAsync()
    {
        var idClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (idClaim == null || !int.TryParse(idClaim.Value, out var userId))
            return null;
        return await _context.Users.FindAsync(userId);
    }
}
