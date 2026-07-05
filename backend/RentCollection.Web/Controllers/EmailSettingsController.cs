using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;

namespace RentCollection.Web.Controllers;

/// <summary>
/// Sprint 25 — admin UI for the SMTP configuration that drives every
/// transactional email the app sends. Admins can edit values + click Test
/// to verify the credentials work before saving.
///
/// Password is the only field treated with care on display: we never
/// echo the saved password back to the form (input rendered with a blank
/// value). If the admin leaves it blank on Save, we preserve the existing
/// stored password; if they type anything, that replaces it. This way you
/// can fix the From-name without re-typing the password.
/// </summary>
[Authorize(Roles = Roles.AnyAdmin)]
[Route("EmailSettings")]
public class EmailSettingsController : Controller
{
    private readonly IEmailSettingsService _settings;
    private readonly ILogger<EmailSettingsController> _logger;

    public EmailSettingsController(IEmailSettingsService settings, ILogger<EmailSettingsController> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var current = await _settings.GetActiveAsync();
        var vm = ToViewModel(current);
        return View(vm);
    }

    [HttpPost("Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(EmailSettingsViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            return View(nameof(Index), vm);
        }

        var current = await _settings.GetActiveAsync();
        var toSave = new EmailSettings
        {
            EmailSettingsId = current.EmailSettingsId,
            Host = vm.Host?.Trim() ?? string.Empty,
            Port = vm.Port,
            EnableSsl = vm.EnableSsl,
            Username = vm.Username?.Trim() ?? string.Empty,
            // Preserve existing password if the admin left the field blank
            // (or types only whitespace by accident — IsNullOrWhiteSpace).
            // The form renders password as empty so we don't leak it back —
            // a blank submission means "no change".
            Password = string.IsNullOrWhiteSpace(vm.Password) ? current.Password : vm.Password,
            FromAddress = vm.FromAddress?.Trim() ?? string.Empty,
            FromName = vm.FromName?.Trim() ?? "ALRTAM Company",
            DryRun = vm.DryRun,
        };

        await _settings.SaveAsync(toSave);
        _logger.LogInformation("Email settings updated by {User}", User.Identity?.Name);

        TempData["SuccessMessage"] = "Email settings saved.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Test-send using the values currently in the form (NOT yet saved).
    /// Lets the admin verify credentials before clicking Save. Returns JSON
    /// so the page can show the result inline without a full reload.
    ///
    /// Rate-limited via the "email-test" policy (3 calls per minute per IP)
    /// to prevent abuse: without this, any admin could fire arbitrary mail
    /// from the corporate From address by spamming the endpoint.
    /// </summary>
    [HttpPost("Test")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("email-test")]
    public async Task<IActionResult> Test(EmailSettingsViewModel vm, string testRecipient)
    {
        if (string.IsNullOrWhiteSpace(testRecipient))
        {
            return Json(new { success = false, message = "Recipient address is required." });
        }

        var current = await _settings.GetActiveAsync();
        // Sprint 25 fix — pass the saved row's ID so the service stamps the
        // outcome (LastTestAt/LastTestResult) on that row. Was previously 0
        // (override path) which silently skipped the stamp.
        var probe = new EmailSettings
        {
            EmailSettingsId = current.EmailSettingsId,
            Host = vm.Host?.Trim() ?? string.Empty,
            Port = vm.Port,
            EnableSsl = vm.EnableSsl,
            Username = vm.Username?.Trim() ?? string.Empty,
            Password = string.IsNullOrWhiteSpace(vm.Password) ? current.Password : vm.Password,
            FromAddress = vm.FromAddress?.Trim() ?? string.Empty,
            FromName = vm.FromName?.Trim() ?? "ALRTAM Company",
            DryRun = vm.DryRun,
        };

        _logger.LogInformation(
            "Email test triggered by {User} → {Recipient} via host {Host}",
            User.Identity?.Name, testRecipient, probe.Host);

        var result = await _settings.SendTestAsync(testRecipient.Trim(), probe, stampOutcome: true);
        return Json(new
        {
            success = result.Success,
            message = result.Message,
            elapsedMs = (int)result.Elapsed.TotalMilliseconds,
        });
    }

    private static EmailSettingsViewModel ToViewModel(EmailSettings s) => new()
    {
        Host = s.Host,
        Port = s.Port,
        EnableSsl = s.EnableSsl,
        Username = s.Username,
        Password = string.Empty, // never echo the saved password
        HasStoredPassword = !string.IsNullOrEmpty(s.Password),
        FromAddress = s.FromAddress,
        FromName = s.FromName,
        DryRun = s.DryRun,
        LastTestAt = s.LastTestAt,
        LastTestResult = s.LastTestResult,
    };
}

public class EmailSettingsViewModel
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string? Password { get; set; }
    /// <summary>Display-only: tells the form to render "password unchanged" hint.</summary>
    public bool HasStoredPassword { get; set; }
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "ALRTAM Company";
    public bool DryRun { get; set; } = true;
    public DateTime? LastTestAt { get; set; }
    public string? LastTestResult { get; set; }
}
