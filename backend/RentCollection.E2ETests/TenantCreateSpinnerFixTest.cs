using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// Regression (2026-07): "Create Tenant loads all the time" — clicking submit on
/// an INVALID form locked both submit buttons with a spinner, then jQuery
/// unobtrusive validation preventDefault()'d the POST, so the lock never cleared
/// (form-helpers.js _wireSubmitLock used to lock unconditionally on 'submit').
///
/// The fix only locks when the form actually validates and clears the lock on an
/// invalid submit. This test proves:
///   A. Invalid submit → buttons are NOT stuck (no .is-loading, not disabled),
///      still on /Tenants/Create, validation errors visible.
///   B. A real browser loads /Tenants fine (the "hang" was a curl/no-gzip artifact).
/// No data is created (invalid submit is a no-op), so nothing to clean up.
/// </summary>
public sealed class TenantCreateSpinnerFixTest : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public TenantCreateSpinnerFixTest(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Invalid_submit_does_not_get_stuck_spinning()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;

        var consoleErrors = new List<string>();
        page.Console += (_, m) => { if (m.Type == "error") consoleErrors.Add(m.Text); };

        await page.GotoAsync("/Tenants/Create", new() { WaitUntil = WaitUntilState.NetworkIdle });

        var createBtn = page.Locator("button.fr-btn-primary").First;
        var saveAddBtn = page.Locator("[data-fr-save-and-add]").First;

        // Submit with an empty/invalid form. jQuery-validate sets novalidate, so the
        // 'submit' event fires (the old bug window) and then validation cancels the POST.
        await createBtn.ClickAsync(new() { NoWaitAfter = true });

        // Give the old bug time to manifest (it would leave the spinner locked forever).
        await page.WaitForTimeoutAsync(2000);

        // A. Buttons must be usable again — NOT stuck in the loading state.
        var createLoading  = await createBtn.GetAttributeAsync("class") ?? "";
        var createDisabled = await createBtn.IsDisabledAsync();
        var saveLoading    = await saveAddBtn.GetAttributeAsync("class") ?? "";
        var saveDisabled   = await saveAddBtn.IsDisabledAsync();

        // Still on the Create page (POST was cancelled by validation), and errors shown.
        var url = page.Url;
        var invalidCount = await page.Locator("input.input-validation-error, .field-validation-error, .validation-summary-errors").CountAsync();

        System.Console.WriteLine(
            $"SPINNERFIX createLoading=[{createLoading.Contains("is-loading")}] createDisabled=[{createDisabled}] " +
            $"saveLoading=[{saveLoading.Contains("is-loading")}] saveDisabled=[{saveDisabled}] " +
            $"url=[{url}] validationMarkers=[{invalidCount}] consoleErrors=[{consoleErrors.Count}]");

        createLoading.Should().NotContain("is-loading", "the Create button must not stay in the loading state after a cancelled (invalid) submit");
        createDisabled.Should().BeFalse("the Create button must be clickable again so the user can fix errors and retry");
        saveLoading.Should().NotContain("is-loading");
        saveDisabled.Should().BeFalse();
        url.Should().Contain("/Tenants/Create", "an invalid submit stays on the form");

        // B. A real browser loads the previously-"hanging" list page fine.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var resp = await page.GotoAsync("/Tenants", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
        sw.Stop();
        System.Console.WriteLine($"TENANTS_LIST status=[{resp?.Status}] loadMs=[{sw.ElapsedMilliseconds}]");
        resp!.Status.Should().Be(200, "a real browser (sends Accept-Encoding: gzip) loads /Tenants fine");
    }
}
