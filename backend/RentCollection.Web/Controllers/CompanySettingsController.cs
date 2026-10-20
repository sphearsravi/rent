using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Application.Branding;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.Controllers;

/// <summary>
/// Company & bank details printed on every invoice, receipt and statement
/// PDF. Stored as a singleton BrandingProfiles row; blank fields fall back
/// to appsettings.json (Branding section). Saving refreshes the in-process
/// BrandingCache so the next PDF uses the new values immediately.
/// </summary>
[Authorize(Roles = Roles.AnyAdmin)]
[Route("CompanySettings")]
public class CompanySettingsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly BrandingCache _cache;
    private readonly ILogger<CompanySettingsController> _logger;

    public CompanySettingsController(ApplicationDbContext db, BrandingCache cache, ILogger<CompanySettingsController> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var row = await _db.BrandingProfiles.AsNoTracking()
            .OrderByDescending(b => b.BrandingProfileId).FirstOrDefaultAsync();
        var eff = _cache.Current;   // what the PDFs print right now (config + row)
        var vm = new CompanySettingsViewModel
        {
            CompanyName = row?.CompanyName ?? eff.CompanyName,
            TagLine = row?.TagLine ?? eff.TagLine,
            ContactAddress = row?.ContactAddress ?? eff.ContactAddress,
            ContactPhone = row?.ContactPhone ?? eff.ContactPhone,
            ContactEmail = row?.ContactEmail ?? eff.ContactEmail,
            Website = row?.Website ?? eff.Website,
            BankName = row?.BankName ?? eff.Bank.Name,
            BankAccountHolder = row?.BankAccountHolder ?? eff.Bank.AccountHolder,
            BankIban = row?.BankIban ?? eff.Bank.Iban,
            BankSwiftBic = row?.BankSwiftBic ?? eff.Bank.SwiftBic,
            FooterTerms = row?.FooterTerms ?? eff.FooterTerms,
            FooterTermsAr = row?.FooterTermsAr ?? eff.FooterTermsAr,
            ModifiedAt = row?.ModifiedAt,
            ModifiedBy = row?.ModifiedBy,
            BankDetailsOnInvoice = !string.IsNullOrWhiteSpace(eff.Bank.Name) || !string.IsNullOrWhiteSpace(eff.Bank.Iban),
        };
        return View(vm);
    }

    [HttpPost("Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(CompanySettingsViewModel vm)
    {
        // IBAN: strip spaces, upper-case, sanity-check the shape (Kuwait IBANs
        // are 30 chars starting with KW, but other countries are allowed).
        var iban = (vm.BankIban ?? "").Replace(" ", "").ToUpperInvariant();
        if (iban.Length > 0 && !Regex.IsMatch(iban, @"^[A-Z]{2}\d{2}[A-Z0-9]{11,30}$"))
            ModelState.AddModelError(nameof(vm.BankIban), "IBAN looks wrong: expected 2 letters, 2 digits, then 11–30 letters/digits (Kuwait: 30 characters starting with KW).");
        var swift = (vm.BankSwiftBic ?? "").Replace(" ", "").ToUpperInvariant();
        if (swift.Length > 0 && !Regex.IsMatch(swift, @"^[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3})?$"))
            ModelState.AddModelError(nameof(vm.BankSwiftBic), "SWIFT/BIC must be 8 or 11 characters, e.g. NBOKKWKW.");
        if (!string.IsNullOrWhiteSpace(vm.BankIban) && string.IsNullOrWhiteSpace(vm.BankName))
            ModelState.AddModelError(nameof(vm.BankName), "Enter the bank name when giving an IBAN.");

        if (!ModelState.IsValid) return View(nameof(Index), vm);

        var row = await _db.BrandingProfiles.OrderByDescending(b => b.BrandingProfileId).FirstOrDefaultAsync();
        if (row == null)
        {
            row = new BrandingProfile { CreatedAt = DateTime.UtcNow };
            _db.BrandingProfiles.Add(row);
        }

        static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        row.CompanyName = Clean(vm.CompanyName);
        row.TagLine = Clean(vm.TagLine);
        row.ContactAddress = Clean(vm.ContactAddress);
        row.ContactPhone = Clean(vm.ContactPhone);
        row.ContactEmail = Clean(vm.ContactEmail);
        row.Website = Clean(vm.Website);
        row.BankName = Clean(vm.BankName);
        row.BankAccountHolder = Clean(vm.BankAccountHolder);
        row.BankIban = iban.Length == 0 ? null : iban;
        row.BankSwiftBic = swift.Length == 0 ? null : swift;
        row.FooterTerms = Clean(vm.FooterTerms);
        row.FooterTermsAr = Clean(vm.FooterTermsAr);
        row.ModifiedAt = DateTime.UtcNow;
        row.ModifiedBy = User.Identity?.Name;

        await _db.SaveChangesAsync();
        _cache.Apply(row);
        _logger.LogInformation("Company settings updated by {User}", User.Identity?.Name);

        TempData["SuccessMessage"] = "Company & bank details saved. New PDFs use them from now on.";
        return RedirectToAction(nameof(Index));
    }
}

public class CompanySettingsViewModel
{
    [MaxLength(200)] public string? CompanyName { get; set; }
    [MaxLength(200)] public string? TagLine { get; set; }
    [MaxLength(300)] public string? ContactAddress { get; set; }
    [MaxLength(50)]  public string? ContactPhone { get; set; }
    [MaxLength(200)] public string? ContactEmail { get; set; }
    [MaxLength(200)] public string? Website { get; set; }
    [MaxLength(200)] public string? BankName { get; set; }
    [MaxLength(200)] public string? BankAccountHolder { get; set; }
    [MaxLength(50)]  public string? BankIban { get; set; }
    [MaxLength(20)]  public string? BankSwiftBic { get; set; }
    [MaxLength(1000)] public string? FooterTerms { get; set; }
    [MaxLength(1000)] public string? FooterTermsAr { get; set; }

    // Display only
    public DateTime? ModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }
    public bool BankDetailsOnInvoice { get; set; }
}
