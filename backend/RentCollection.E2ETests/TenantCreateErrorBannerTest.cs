using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;
using RentCollection.E2ETests.Pages;

namespace RentCollection.E2ETests;

/// <summary>
/// Guard for the top-of-page error banner on /Tenants/Create. Forces a
/// SERVER-side validation error (invalid email, with HTML5 validation
/// bypassed) and asserts the page re-renders with a visible error banner at
/// the top listing the problem — instead of silently bouncing.
/// </summary>
public sealed class TenantCreateErrorBannerTest : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public TenantCreateErrorBannerTest(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Server_validation_error_shows_banner_on_top()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var t = new TenantCreatePage(page);
        await t.GoToAsync();

        var suffix = AdminSession.UniqueSuffix();
        // Fill everything valid EXCEPT the email, which is malformed.
        await t.FirstName.FillAsync("Banner");
        await t.LastName.FillAsync($"Probe{suffix}");
        await t.Email.FillAsync("not-an-email");           // fails [EmailAddress] server-side
        await t.Phone.FillAsync("99000000");
        await t.Paci.FillAsync("123456789012");
        await t.BuildingPaci.FillAsync("NO-MATCH-BUILDING");
        await t.SetCompanyIdAsync("1");

        // Submit via form.submit() — this bypasses BOTH HTML5 validation and
        // jQuery-unobtrusive (which hooks the submit *event*), so the malformed
        // email reaches the server and exercises the ModelState → banner path.
        await page.EvaluateAsync("() => document.getElementById('tenantCreateForm').submit()");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var banner = page.Locator("#formErrorBanner");
        // Wait for the server-rendered banner rather than racing the reload.
        try { await banner.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 8000 }); }
        catch (TimeoutException) { /* assertions below report the failure with detail */ }
        var count = await banner.CountAsync();
        var visible = count > 0 && await banner.IsVisibleAsync();
        var bannerText = count > 0 ? (await banner.InnerTextAsync()) : "";
        var content = await page.ContentAsync();

        Console.WriteLine($"BANNER_TEST url=[{page.Url}] count=[{count}] visible=[{visible}] " +
            $"hasBannerId=[{content.Contains("formErrorBanner")}] hasPleaseFix=[{content.Contains("Please fix")}] " +
            $"hasInvalidEmail=[{content.Contains("Invalid email address")}] hasErrBannerClass=[{content.Contains("fr-error-banner")}] " +
            $"text=[{bannerText.Replace("\n", " / ")}]");

        page.Url.Should().Contain("/Tenants/Create", "a validation error must keep us on the form");
        visible.Should().BeTrue("the error banner must be shown at the top when the server rejects the form");
        bannerText.Should().Contain("Please fix", "the banner must carry the fix-it heading");
        bannerText.Should().Contain("Invalid email address", "the banner must list the actual field error");

        // And it must be positioned above the first field (i.e. 'on top').
        var bannerBox = await banner.BoundingBoxAsync();
        var firstNameBox = await t.FirstName.BoundingBoxAsync();
        bannerBox!.Y.Should().BeLessThan(firstNameBox!.Y, "the banner must sit above the form fields");
    }
}
