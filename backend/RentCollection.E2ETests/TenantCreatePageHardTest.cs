using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;
using RentCollection.Infrastructure.Data;

namespace RentCollection.E2ETests;

/// <summary>
/// HARD guard (2026-07) for /Tenants/Create after the Google Gemini Civil ID
/// scan was removed. One test, many gates — all must hold:
///   A. The Gemini scan UI is GONE (button, consent, file input, "Gemini" text).
///   B. No JS console errors on load or through the flow (dead refs would throw).
///   C. The LOCAL Civil ID parser still pre-fills DOB from a valid number.
///   D. The full create workflow still works end-to-end (form valid → redirect →
///      DB row → appears in listing).
///   E. Clear All still wipes the form.
/// Self-cleans the tenant it creates.
/// </summary>
public sealed class TenantCreatePageHardTest : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public TenantCreatePageHardTest(E2EFixture fixture) => _fixture = fixture;

    private static string ConnString =>
        Environment.GetEnvironmentVariable("RC_E2E_DB")
        ?? @"Server=RAVIWST\SQLEXPRESS;Database=RentCollectionDB;Trusted_Connection=true;TrustServerCertificate=true;";

    [Fact]
    public async Task Gemini_removed_and_page_still_fully_works()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;

        var consoleErrors = new List<string>();
        page.Console += (_, m) => { if (m.Type == "error") consoleErrors.Add(m.Text); };
        page.PageError += (_, e) => consoleErrors.Add("PAGEERR: " + e);

        await page.GotoAsync("/Tenants/Create", new() { WaitUntil = WaitUntilState.NetworkIdle });

        // ---- A. Gemini scan UI is gone ----
        var scanBtn = await page.Locator("#scanCivilIdBtn").CountAsync();
        var consent = await page.Locator("#civilIdConsent").CountAsync();
        var fileInp = await page.Locator("#civilIdImageFile").CountAsync();
        var body = await page.ContentAsync();
        var geminiMentions = System.Text.RegularExpressions.Regex.Matches(body, "Gemini").Count;
        var scanCardText = body.Contains("Scan card");

        // ---- C. Local Civil ID parser still enriches (DOB pre-fill) ----
        // Expand "More details" so #DateOfBirth is interactable, then type a
        // structurally-valid Kuwait Civil ID (leading 2 => 19xx, YYMMDD).
        await page.Locator("[data-fr-toggle='moreDetails']").ClickAsync();
        await page.Locator("#civilIdInput").FillAsync("290010112345");
        await page.WaitForTimeoutAsync(1200); // allow the /api/civilid/parse round-trip
        var dobAfterParse = await page.Locator("#DateOfBirth").InputValueAsync();

        // ---- E. Clear All still works ----
        await page.Locator("#FirstName").FillAsync("WipeMe");
        await page.Locator("#clearAllBtn").ClickAsync();
        var firstAfterClear = await page.Locator("#FirstName").InputValueAsync();
        var civilAfterClear = await page.Locator("#civilIdInput").InputValueAsync();

        // ---- D. Full create workflow still works ----
        var suffix = AdminSession.UniqueSuffix();
        var firstName = "HardTest";
        var lastName = $"Page{suffix}";
        var paci = Math.Abs(Guid.NewGuid().GetHashCode()).ToString().PadLeft(12, '0').Substring(0, 12);

        await page.Locator("#FirstName").FillAsync(firstName);
        await page.Locator("#LastName").FillAsync(lastName);
        await page.Locator("#civilIdInput").FillAsync(paci);
        await page.Locator("#BuildingPACINumber").FillAsync("PACI-FX-001");
        await page.Locator("#Email").FillAsync($"hard+{suffix}@example.com");
        await page.Locator("#Phone").FillAsync("99000777");
        await page.EvaluateAsync(@"() => { var c = document.getElementById('CompanyId'); if (c) c.value = '1'; }");

        var validity = await page.EvaluateAsync<string>(@"() => {
            var f = document.getElementById('tenantCreateForm');
            if (!f) return 'NO_FORM';
            if (f.checkValidity()) return 'VALID';
            return 'INVALID -> ' + [...f.elements].filter(e => e.willValidate && !e.checkValidity())
                .map(e => (e.name||e.id) + ': ' + e.validationMessage).join(' | ');
        }");

        await page.Locator("button.fr-btn-primary").First.ClickAsync(new() { NoWaitAfter = true });
        try { await page.WaitForURLAsync(u => !u.Contains("/Tenants/Create"), new() { Timeout = 20_000 }); }
        catch (TimeoutException) { }
        var landedUrl = page.Url;

        var opts = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnString).Options;
        int dbId = 0;
        await using (var db = new ApplicationDbContext(opts))
        {
            var row = await db.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.LastName == lastName && t.FirstName == firstName);
            if (row != null) dbId = row.TenantId;
        }

        await page.GotoAsync("/Tenants", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var listed = (await page.ContentAsync()).Contains(lastName);

        Console.WriteLine("HARD_PAGE_REPORT " +
            $"scanBtn=[{scanBtn}] consent=[{consent}] fileInp=[{fileInp}] geminiMentions=[{geminiMentions}] scanCardText=[{scanCardText}] " +
            $"dobAfterParse=[{dobAfterParse}] firstAfterClear=[{firstAfterClear}] civilAfterClear=[{civilAfterClear}] " +
            $"validity=[{validity}] landedUrl=[{landedUrl}] dbId=[{dbId}] listed=[{listed}] " +
            $"consoleErrors=[{string.Join(" ;; ", consoleErrors)}]");

        try
        {
            // A — Gemini gone
            scanBtn.Should().Be(0, "the Gemini Scan card button must be removed");
            consent.Should().Be(0, "the Gemini consent checkbox must be removed");
            fileInp.Should().Be(0, "the Gemini image file input must be removed");
            geminiMentions.Should().Be(0, "no 'Gemini' text should remain on the page");
            scanCardText.Should().BeFalse("the 'Scan card' control must be gone");
            // B — no console errors (dead element refs would throw)
            consoleErrors.Should().BeEmpty("removing the scan JS must not leave broken references");
            // C — local parser still enriches
            dobAfterParse.Should().NotBeNullOrEmpty("the local Civil ID parser must still pre-fill DOB");
            // E — Clear All works
            firstAfterClear.Should().BeEmpty("Clear All must still wipe fields");
            civilAfterClear.Should().BeEmpty("Clear All must still wipe the Civil ID field");
            // D — full workflow works
            validity.Should().Be("VALID", "the filled form must pass HTML5 validation");
            landedUrl.Should().NotContain("/Tenants/Create", "valid submit must redirect off Create");
            dbId.Should().BeGreaterThan(0, "the tenant must be persisted");
            listed.Should().BeTrue("the tenant must appear in the listing");
        }
        finally
        {
            if (dbId > 0)
            {
                await using var db = new ApplicationDbContext(opts);
                await db.Tenants.Where(t => t.TenantId == dbId).ExecuteDeleteAsync();
            }
        }
    }
}
