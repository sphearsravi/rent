using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
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
    private readonly RentCollection.Web.Services.ILoginThrottle _loginThrottle;
    private readonly RentCollection.Web.Services.TotpService _totp;
    private readonly Microsoft.AspNetCore.DataProtection.IDataProtector _mfaProtector;

    public AccountController(
        ApplicationDbContext context,
        ILogger<AccountController> logger,
        RentCollection.Web.Services.ILoginThrottle loginThrottle,
        RentCollection.Web.Services.TotpService totp,
        Microsoft.AspNetCore.DataProtection.IDataProtectionProvider dataProtection)
    {
        _context = context;
        _logger = logger;
        _passwordHasher = new PasswordHasher();
        _loginThrottle = loginThrottle;
        _totp = totp;
        // Purpose-scoped protectors: one carries the "password step passed, awaiting
        // TOTP" state between the two login requests; one carries the pending secret
        // during enrolment. Both are short-lived, server-signed — never trust a
        // client-supplied userId/secret for these steps.
        _mfaProtector = dataProtection.CreateProtector("RentCollection.Mfa.v1");
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
            // SECURITY (audit H2): brute-force lockout, keyed on the submitted
            // identifier so it survives IP rotation and the shared-hosting proxy.
            if (_loginThrottle.IsLockedOut(model.Email ?? string.Empty, out var retryAfter))
            {
                _logger.LogWarning("Login blocked for locked-out identifier (retryAfter {Mins}m).",
                    (int)Math.Ceiling(retryAfter.TotalMinutes));
                ModelState.AddModelError(string.Empty,
                    "Too many failed attempts. Please try again later.");
                return View(model);
            }

            // Try to find user in custom Users table by email or username
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == model.Email || u.Username == model.Email);

            if (user == null)
            {
                _loginThrottle.RecordFailure(model.Email ?? string.Empty);
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return View(model);
            }

            // Verify password using custom password hasher
            if (!_passwordHasher.VerifyPassword(model.Password, user.PasswordHash))
            {
                _loginThrottle.RecordFailure(model.Email ?? string.Empty);
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return View(model);
            }

            // SECURITY: the deactivated check runs AFTER the password check and
            // returns the same generic message. Previously it ran first with a
            // distinct "deactivated" message, which (a) confirmed the account
            // existed to anyone who guessed the identifier and (b) bypassed the
            // failure throttle entirely.
            if (!user.IsActive)
            {
                _logger.LogWarning("Login attempt for deactivated account {UserId}.", user.UserId);
                _loginThrottle.RecordFailure(model.Email ?? string.Empty);
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return View(model);
            }

            // Successful credential check — clear the failure counter.
            _loginThrottle.Reset(model.Email ?? string.Empty);

            // TOTP MFA second factor. If MFA is enabled, the password step alone
            // must NOT sign the user in — issue a short-lived, server-signed
            // "pending" token (proving the password was verified) and hand off to
            // the code challenge. The trusted userId lives ONLY in the signed
            // cookie, never in a client field.
            if (user.MfaEnabled && !string.IsNullOrEmpty(user.MfaSecret))
            {
                var payload = $"{user.UserId}|{DateTime.UtcNow.Ticks}|{(model.RememberMe ? 1 : 0)}|{model.ReturnUrl}";
                Response.Cookies.Append("MfaPending", _mfaProtector.Protect(payload), new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    Expires = DateTimeOffset.UtcNow.AddMinutes(5),
                    IsEssential = true,
                });
                return RedirectToAction(nameof(LoginMfa));
            }

            await SignInUserAsync(user, model.RememberMe);
            _logger.LogInformation("User {Email} logged in successfully.", user.Email);
            return RedirectAfterLogin(model.ReturnUrl);
        }

        return View(model);
    }

    /// <summary>Builds the auth cookie for a fully-authenticated user.</summary>
    private async Task SignInUserAsync(User user, bool rememberMe)
    {
        // The `companyId` claim backs the company-scope EF query filter — without
        // it, logged-in users see no rows.
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
            claims.Add(new Claim("companyId", user.CompanyId.Value.ToString()));

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = rememberMe,
            ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddHours(12),
            AllowRefresh = !rememberMe,
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);

        user.LastLogin = DateTime.UtcNow;
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Post-login redirect. Never honors a ReturnUrl pointing back at the auth
    /// endpoints (a stale /Account/Logout would bounce the user straight out).
    /// </summary>
    private IActionResult RedirectAfterLogin(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl)
            && Url.IsLocalUrl(returnUrl)
            && returnUrl.IndexOf("/Account/Logout", StringComparison.OrdinalIgnoreCase) < 0
            && returnUrl.IndexOf("/Account/Login", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction("Index", "Dashboard");
    }

    // ── TOTP MFA: login challenge (second factor) ──────────────────────────

    [HttpGet]
    [AllowAnonymous]
    public IActionResult LoginMfa()
    {
        if (!Request.Cookies.ContainsKey("MfaPending"))
            return RedirectToAction(nameof(Login));
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginMfa(string code)
    {
        if (!Request.Cookies.TryGetValue("MfaPending", out var protectedPayload))
            return RedirectToAction(nameof(Login));

        string payload;
        try { payload = _mfaProtector.Unprotect(protectedPayload); }
        catch { Response.Cookies.Delete("MfaPending"); return RedirectToAction(nameof(Login)); }

        var parts = payload.Split('|');
        if (parts.Length < 4 || !int.TryParse(parts[0], out var userId) || !long.TryParse(parts[1], out var ticks))
        {
            Response.Cookies.Delete("MfaPending");
            return RedirectToAction(nameof(Login));
        }
        // 5-minute validity on the password→code window.
        if (new DateTime(ticks, DateTimeKind.Utc) < DateTime.UtcNow.AddMinutes(-5))
        {
            Response.Cookies.Delete("MfaPending");
            ModelState.AddModelError(string.Empty, "Your session expired. Please sign in again.");
            return RedirectToAction(nameof(Login));
        }
        var rememberMe = parts[2] == "1";
        var returnUrl = parts.Length > 3 ? string.Join('|', parts.Skip(3)) : null;

        // Throttle code attempts per account (brute-force on 6 digits).
        var throttleKey = $"mfa:{userId}";
        if (_loginThrottle.IsLockedOut(throttleKey, out _))
        {
            ModelState.AddModelError(string.Empty, "Too many attempts. Please try again later.");
            return View();
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null || !user.MfaEnabled || string.IsNullOrEmpty(user.MfaSecret))
        {
            Response.Cookies.Delete("MfaPending");
            return RedirectToAction(nameof(Login));
        }

        if (!_totp.Verify(user.MfaSecret, code ?? string.Empty))
        {
            _loginThrottle.RecordFailure(throttleKey);
            ModelState.AddModelError(string.Empty, "Invalid authentication code.");
            return View();
        }

        _loginThrottle.Reset(throttleKey);
        Response.Cookies.Delete("MfaPending");
        await SignInUserAsync(user, rememberMe);
        _logger.LogInformation("User {Email} logged in successfully (MFA).", user.Email);
        return RedirectAfterLogin(returnUrl);
    }

    // ── TOTP MFA: self-service enrolment / management ──────────────────────

    private int? CurrentUserId()
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : (int?)null;

    private static string BuildQrDataUri(string text)
    {
        using var gen = new QRCoder.QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.Q);
        var png = new QRCoder.PngByteQRCode(data).GetGraphic(6);
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Security()
    {
        var userId = CurrentUserId();
        if (userId == null) return RedirectToAction(nameof(Login));
        var user = await _context.Users.FindAsync(userId.Value);
        if (user == null) return RedirectToAction(nameof(Login));
        ViewBag.MfaEnabled = user.MfaEnabled;
        return View();
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> EnableMfa()
    {
        var userId = CurrentUserId();
        if (userId == null) return RedirectToAction(nameof(Login));
        var user = await _context.Users.FindAsync(userId.Value);
        if (user == null) return RedirectToAction(nameof(Login));
        if (user.MfaEnabled)
        {
            TempData["MfaMessage"] = "Two-factor authentication is already enabled.";
            return RedirectToAction(nameof(Security));
        }

        var secret = _totp.GenerateSecret();
        // Hold the pending (unconfirmed) secret in a short-lived signed cookie tied
        // to this user — it's only committed to the account once a valid code proves
        // the authenticator was set up correctly.
        Response.Cookies.Append("MfaSetup", _mfaProtector.Protect($"{userId}|{secret}"), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddMinutes(10),
            IsEssential = true,
        });

        ViewBag.Secret = secret;
        ViewBag.QrDataUri = BuildQrDataUri(_totp.BuildOtpAuthUri(secret, user.Email));
        return View();
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EnableMfa(string code)
    {
        var userId = CurrentUserId();
        if (userId == null) return RedirectToAction(nameof(Login));
        var user = await _context.Users.FindAsync(userId.Value);
        if (user == null) return RedirectToAction(nameof(Login));

        if (!Request.Cookies.TryGetValue("MfaSetup", out var prot))
        {
            TempData["MfaError"] = "Setup expired. Please start again.";
            return RedirectToAction(nameof(EnableMfa));
        }
        string payload;
        try { payload = _mfaProtector.Unprotect(prot); }
        catch { Response.Cookies.Delete("MfaSetup"); return RedirectToAction(nameof(EnableMfa)); }

        var parts = payload.Split('|');
        if (parts.Length < 2 || parts[0] != userId.Value.ToString())
        {
            Response.Cookies.Delete("MfaSetup");
            return RedirectToAction(nameof(EnableMfa));
        }
        var secret = parts[1];

        if (!_totp.Verify(secret, code ?? string.Empty))
        {
            // Re-render with the SAME secret so the user can retry without rescanning.
            ViewBag.Secret = secret;
            ViewBag.QrDataUri = BuildQrDataUri(_totp.BuildOtpAuthUri(secret, user.Email));
            ModelState.AddModelError(string.Empty, "That code didn't match. Check the time on your device and try again.");
            return View();
        }

        user.MfaSecret = secret;
        user.MfaEnabled = true;
        user.ModifiedDate = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        Response.Cookies.Delete("MfaSetup");
        _logger.LogInformation("MFA enabled for {Email}.", user.Email);
        TempData["MfaMessage"] = "Two-factor authentication is now enabled.";
        return RedirectToAction(nameof(Security));
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DisableMfa(string password)
    {
        var userId = CurrentUserId();
        if (userId == null) return RedirectToAction(nameof(Login));
        var user = await _context.Users.FindAsync(userId.Value);
        if (user == null) return RedirectToAction(nameof(Login));

        // Require the account password to disable MFA — a hijacked session alone
        // must not be able to remove the second factor.
        if (!_passwordHasher.VerifyPassword(password ?? string.Empty, user.PasswordHash))
        {
            TempData["MfaError"] = "Incorrect password — two-factor authentication was not disabled.";
            return RedirectToAction(nameof(Security));
        }

        user.MfaEnabled = false;
        user.MfaSecret = null;
        user.ModifiedDate = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        _logger.LogInformation("MFA disabled for {Email}.", user.Email);
        TempData["MfaMessage"] = "Two-factor authentication has been disabled.";
        return RedirectToAction(nameof(Security));
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
        // Shared password policy (same rule as admin create / reset).
        var pwErr = RentCollection.Web.Services.PasswordPolicy.Validate(newPassword);
        if (pwErr != null)
        {
            TempData["ErrorMessage"] = pwErr;
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
