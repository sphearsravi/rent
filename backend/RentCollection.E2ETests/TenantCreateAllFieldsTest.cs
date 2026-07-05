using System.Text;
using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// Field-by-field audit of /Tenants/Create (2026-07). Exercises every input,
/// select, file field, button and behavior on the page and prints a PASS/FAIL
/// line for each. Read the FIELDCHECK lines in the console output.
/// No tenant is created (only an invalid submit + a Clear All), so nothing to clean.
/// </summary>
public sealed class TenantCreateAllFieldsTest : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public TenantCreateAllFieldsTest(E2EFixture fixture) => _fixture = fixture;

    private readonly StringBuilder _log = new();
    private int _pass, _fail;
    private void Check(string name, bool ok, string detail = "")
    {
        if (ok) _pass++; else _fail++;
        _log.AppendLine($"FIELDCHECK [{(ok ? "PASS" : "FAIL")}] {name}{(detail == "" ? "" : "  — " + detail)}");
    }

    [Fact]
    public async Task Every_field_on_create_page()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var consoleErrors = new List<string>();
        page.Console += (_, m) => { if (m.Type == "error") consoleErrors.Add(m.Text); };
        page.PageError += (_, e) => consoleErrors.Add("PAGEERR: " + e);

        await page.GotoAsync("/Tenants/Create", new() { WaitUntil = WaitUntilState.NetworkIdle });

        // ── 1. PRESENCE of every field ───────────────────────────────────
        async Task Present(string label, string selector)
            => Check($"present: {label}", await page.Locator(selector).CountAsync() > 0, selector);

        await Present("First Name", "#FirstName");
        await Present("Last Name", "#LastName");
        await Present("Email", "#Email");
        await Present("Phone", "#Phone");
        await Present("Set password (InitialPassword)", "input[name='InitialPassword']");
        await Present("Civil ID", "#civilIdInput");
        await Present("Building PACI", "#BuildingPACINumber");
        await Present("Date of Birth", "#DateOfBirth");
        await Present("Employment Status", "#EmploymentStatus");
        await Present("Emergency Contact", "#EmergencyContact");
        await Present("Emergency Phone", "#EmergencyPhone");
        await Present("Document Type", "select[name='DocumentTypes[]']");
        await Present("Document File", "input[name='Documents[]']");
        await Present("Create button", "button.fr-btn-primary");
        await Present("Save & add another", "[data-fr-save-and-add]");
        await Present("Clear All", "#clearAllBtn");
        await Present("Cancel", "a.fr-btn-ghost");
        await Present("Add another document", "#add-document-btn");
        // Removed fields must be ABSENT:
        Check("removed: Government ID field is gone", await page.Locator("[name='GovernmentId']").CountAsync() == 0);
        Check("removed: Property field is gone", await page.Locator("#PropertyId").CountAsync() == 0);
        Check("removed: Unit field is gone", await page.Locator("#UnitId").CountAsync() == 0);
        Check("removed: Property & Unit section title gone",
            !(await page.ContentAsync()).Contains("Property &amp; Unit"));

        // ── 2. REQUIRED attribute on the 5 essentials ────────────────────
        async Task Required(string label, string selector, bool expected)
        {
            var has = await page.Locator(selector).GetAttributeAsync("required") != null;
            Check($"required={expected}: {label}", has == expected, $"actual required={has}");
        }
        await Required("First Name", "#FirstName", true);
        await Required("Last Name", "#LastName", true);
        await Required("Phone", "#Phone", true);
        await Required("Civil ID", "#civilIdInput", true);
        await Required("Building PACI", "#BuildingPACINumber", true);
        await Required("Email (optional)", "#Email", false);

        // ── 3. SUBMIT EMPTY → required validation fires, no stuck spinner ─
        var createBtn = page.Locator("button.fr-btn-primary").First;
        await createBtn.ClickAsync();
        await page.WaitForTimeoutAsync(1200);
        var stillOnForm = page.Url.Contains("/Tenants/Create");
        var invalidCount = await page.Locator(".input-validation-error").CountAsync();
        var createStuck = (await createBtn.GetAttributeAsync("class") ?? "").Contains("is-loading");
        Check("empty submit stays on form (validation blocked POST)", stillOnForm);
        Check("empty submit flags invalid required fields", invalidCount >= 4, $"{invalidCount} fields flagged");
        Check("empty submit does NOT get stuck spinning", !createStuck);

        // ── 4. CIVIL ID: <12 rejected, 12 accepted, no checksum nag ──────
        var civil = page.Locator("#civilIdInput");
        await civil.FillAsync("12345");
        await createBtn.ClickAsync(); await page.WaitForTimeoutAsync(500);
        var shortInvalid = (await civil.GetAttributeAsync("class") ?? "").Contains("input-validation-error");
        Check("Civil ID: 5 digits rejected", shortInvalid);

        await civil.FillAsync("296010112345");           // 12 digits, encodes 1996-01-01
        await civil.BlurAsync();
        await page.WaitForTimeoutAsync(1500);            // let the /api/civilid/parse call return
        var civilClass = await civil.GetAttributeAsync("class") ?? "";
        var civil12Valid = !civilClass.Contains("input-validation-error");
        var statusText = (await page.Locator("#civilIdStatus").TextContentAsync() ?? "").Trim();
        var dobVal = await page.Locator("#DateOfBirth").InputValueAsync();
        Check("Civil ID: 12 digits accepted (no format error)", civil12Valid);
        Check("Civil ID: NO 'Checksum mismatch' warning", !statusText.Contains("Checksum mismatch", StringComparison.OrdinalIgnoreCase), $"status='{statusText}'");
        Check("Civil ID: DOB auto-filled from number", !string.IsNullOrEmpty(dobVal), $"DOB='{dobVal}'");

        // ── 5. EMAIL format validation ───────────────────────────────────
        await page.Locator("#Email").FillAsync("not-an-email");
        await createBtn.ClickAsync(); await page.WaitForTimeoutAsync(500);
        var emailInvalid = (await page.Locator("#Email").GetAttributeAsync("class") ?? "").Contains("input-validation-error");
        Check("Email: bad address rejected", emailInvalid);
        await page.Locator("#Email").FillAsync("good@example.com");
        await createBtn.ClickAsync(); await page.WaitForTimeoutAsync(400);
        var emailOkNow = !(await page.Locator("#Email").GetAttributeAsync("class") ?? "").Contains("input-validation-error");
        Check("Email: valid address accepted", emailOkNow);

        // ── 6. MORE DETAILS expands (DOB/employment/emergency) ───────────
        if (await page.Locator("[data-fr-toggle='moreDetails']").CountAsync() > 0)
            await page.Locator("[data-fr-toggle='moreDetails']").ClickAsync();
        await page.WaitForTimeoutAsync(400);

        // ── 7. EMPLOYMENT STATUS select options ──────────────────────────
        var empOpts = await page.Locator("#EmploymentStatus option").CountAsync();
        Check("Employment Status has options", empOpts >= 5, $"{empOpts} options");

        // ── 8. DOCUMENTS: add-another adds a row ─────────────────────────
        var rowsBefore = await page.Locator(".document-row").CountAsync();
        await page.Locator("#add-document-btn").ClickAsync();
        await page.WaitForTimeoutAsync(300);
        var rowsAfter = await page.Locator(".document-row").CountAsync();
        Check("Add another document adds a row", rowsAfter == rowsBefore + 1, $"{rowsBefore}→{rowsAfter}");
        var docTypeOpts = await page.Locator("select[name='DocumentTypes[]']").First.Locator("option").CountAsync();
        Check("Document Type has options", docTypeOpts >= 9, $"{docTypeOpts} options");

        // ── 9. CLEAR ALL wipes fields ────────────────────────────────────
        await page.Locator("#FirstName").FillAsync("WipeMe");
        await page.Locator("#Phone").FillAsync("99887766");
        await page.Locator("#clearAllBtn").ClickAsync();
        await page.WaitForTimeoutAsync(500);
        var fn = await page.Locator("#FirstName").InputValueAsync();
        var civ = await page.Locator("#civilIdInput").InputValueAsync();
        Check("Clear All wipes First Name", fn == "", $"'{fn}'");
        Check("Clear All wipes Civil ID", civ == "", $"'{civ}'");

        // ── 10. No JS console errors throughout ──────────────────────────
        Check("No JS console errors", consoleErrors.Count == 0, string.Join(" ;; ", consoleErrors.Take(3)));

        System.Console.WriteLine(_log.ToString());
        System.Console.WriteLine($"FIELDCHECK_SUMMARY pass={_pass} fail={_fail}");
        _fail.Should().Be(0, "every field/behavior check must pass:\n" + _log);
    }
}
