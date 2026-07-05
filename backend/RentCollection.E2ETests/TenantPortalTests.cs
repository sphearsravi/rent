using FluentAssertions;
using Microsoft.Playwright;

namespace RentCollection.E2ETests;

/// <summary>
/// Browser-tier coverage of the tenant portal entry point. The portal is the
/// public-facing face of the system — a regression here means tenants can't
/// pay their rent online. We keep this suite small and anonymous-tier only;
/// a full authenticated journey (login → view invoice → download PDF) belongs
/// in a future sprint where a seeded test tenant is provisioned with known
/// credentials.
/// </summary>
public sealed class TenantPortalTests : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public TenantPortalTests(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Tenant_login_page_renders_civil_id_form()
    {
        await using var ctx = await _fixture.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync("/TenantPortal/Login");

        // Civil ID is the tenant's login key — losing this input breaks
        // every tenant's ability to sign in.
        await page.Locator("#civilId").IsVisibleAsync()
            .ContinueWith(t => t.Result.Should().BeTrue("#civilId is the tenant login key"));
        await page.Locator("#password").IsVisibleAsync()
            .ContinueWith(t => t.Result.Should().BeTrue());
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign In" }).IsVisibleAsync()
            .ContinueWith(t => t.Result.Should().BeTrue());
    }

    [Fact]
    public async Task Tenant_login_with_wrong_civil_id_shows_error_not_redirect()
    {
        await using var ctx = await _fixture.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync("/TenantPortal/Login");

        await page.Locator("#civilId").FillAsync("000000000000");
        await page.Locator("#password").FillAsync("wrong-password");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign In" }).ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wrong creds must keep the tenant on the login page with an error,
        // not silently redirect anywhere. Preventing this regression keeps
        // a misconfigured route from letting unauth users in by accident.
        page.Url.Should().Contain("/TenantPortal/Login");
        var body = (await page.ContentAsync()).ToLowerInvariant();
        var matched = body.Contains("invalid") || body.Contains("incorrect")
                    || body.Contains("not found") || body.Contains("wrong");
        matched.Should().BeTrue(
            "an auth-failure response must surface some error text — silent " +
            "form-re-render is indistinguishable from a successful login");
    }
}
