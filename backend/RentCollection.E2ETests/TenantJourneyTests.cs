using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// Authenticated tenant journey — sign in, see dashboard, download an
/// invoice PDF. Uses <see cref="TenantSeedFixture"/> to ensure a stable
/// known-credentials tenant exists before the suite runs.
///
/// These tests share the seeded tenant across the class; they don't mutate
/// it, so cross-test pollution is impossible.
/// </summary>
public sealed class TenantJourneyTests
    : IClassFixture<E2EFixture>, IClassFixture<TenantSeedFixture>
{
    private readonly E2EFixture _e2e;
    private readonly TenantSeedFixture _tenant;

    public TenantJourneyTests(E2EFixture e2e, TenantSeedFixture tenant)
    {
        _e2e = e2e;
        _tenant = tenant;
    }

    /// <summary>
    /// Helper: open a fresh context, sign in as the seeded tenant, return
    /// the page once we've landed on the post-login screen.
    /// </summary>
    private async Task<(IBrowserContext ctx, IPage page)> SignInAsync()
    {
        var ctx = await _e2e.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync("/TenantPortal/Login");
        await page.Locator("#civilId").FillAsync(TenantSeedFixture.CivilId);
        await page.Locator("#password").FillAsync(TenantSeedFixture.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign In" }).ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        if (page.Url.Contains("/TenantPortal/Login"))
        {
            // Surface the on-page error so the failure message is "wrong
            // creds" not "selector not found".
            var html = await page.ContentAsync();
            throw new InvalidOperationException(
                "Tenant sign-in did not redirect away from /TenantPortal/Login. " +
                "Check the seeded tenant exists with the expected Civil ID + password.\n" +
                $"Page chrome:\n{html.Substring(0, Math.Min(800, html.Length))}");
        }

        return (ctx, page);
    }

    [Fact]
    public async Task Tenant_login_with_known_credentials_lands_on_dashboard()
    {
        var (ctx, page) = await SignInAsync();
        await using var _ = ctx;

        page.Url.Should().Contain("/TenantPortal/Dashboard");
        var body = await page.ContentAsync();
        body.Should().Contain("E2E",
            "the seeded tenant's first name renders in the post-login chrome");
    }

    [Fact]
    public async Task Tenant_can_navigate_to_payment_history()
    {
        var (ctx, page) = await SignInAsync();
        await using var _ = ctx;

        await page.GotoAsync("/TenantPortal/PaymentHistory");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        page.Url.Should().Contain("/TenantPortal/PaymentHistory");
        // The page must render — not redirect to login (session lost) or 500.
        var title = await page.TitleAsync();
        title.Should().NotContain("Error");
    }

    [Fact]
    public async Task Tenant_portal_page_links_to_invoice_pdf_endpoint()
    {
        // The invoice PDF API (/api/tenant/{id}/invoices/{id}/invoice.pdf)
        // authorises via JWT (Flutter / React clients) OR an admin cookie —
        // tenant portal sessions don't qualify. Production tenants reach
        // their PDF by opening the React app, not the Razor portal.
        //
        // What we can prove from a Razor browser session is that the portal
        // pages *embed the correct URL*, so the contract between portal
        // and API doesn't drift. The MakePayment view is where this link
        // appears today.
        var (ctx, page) = await SignInAsync();
        await using var _ = ctx;

        await page.GotoAsync("/TenantPortal/MakePayment");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var html = await page.ContentAsync();

        // The link template embeds the API path — losing this means the
        // tenant has no way to reach their invoice PDF from the portal.
        html.Should().Contain("/api/tenant/",
            "the MakePayment view must link to the tenant invoice-PDF API path");
        html.Should().Contain("/invoice.pdf",
            "the link must point at the PDF endpoint specifically");
    }

    [Fact]
    public async Task Invoice_pdf_endpoint_rejects_anonymous_access()
    {
        // IDOR + auth-gate guard. From a completely unauthenticated context,
        // the PDF endpoint must NOT return a PDF — anything from a redirect
        // to a 401 is acceptable proof of the gate. A 200 with PDF bytes
        // would be a critical IDOR vulnerability.
        await using var ctx = await _e2e.NewContextAsync();
        var page = await ctx.NewPageAsync();

        var response = await page.APIRequest.GetAsync(
            $"/api/tenant/{_tenant.TenantId}/invoices/{_tenant.InvoiceId}/invoice.pdf");

        // Either an explicit reject code OR a redirect to the cookie-login
        // (which returns 200 text/html after Playwright follows it) is
        // acceptable. The disqualifying signal is "200 with application/pdf".
        var ct = response.Headers.GetValueOrDefault("content-type", "");
        var isPdf = ct.StartsWith("application/pdf", StringComparison.OrdinalIgnoreCase);
        isPdf.Should().BeFalse(
            "an unauthenticated request must NOT receive PDF bytes — got " +
            $"HTTP {response.Status} content-type='{ct}'");
    }

    // ───────────── Sprint 19 — deeper tenant portal journey ─────────────

    [Fact]
    public async Task Tenant_can_navigate_to_my_details_page()
    {
        var (ctx, page) = await SignInAsync();
        await using var _ = ctx;

        var response = await page.GotoAsync("/TenantPortal/MyDetails",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        response!.Status.Should().Be(200);

        page.Url.Should().Contain("/TenantPortal/MyDetails");
        var body = await page.ContentAsync();
        body.Should().Contain(TenantSeedFixture.CivilId,
            "MyDetails must surface the tenant's Civil ID for verification");
    }

    [Fact]
    public async Task Tenant_dashboard_lists_active_lease_summary()
    {
        var (ctx, page) = await SignInAsync();
        await using var _ = ctx;

        await page.GotoAsync("/TenantPortal/Dashboard",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var body = await page.ContentAsync();

        // The fixture seeds E2E Test Tower / Unit E2E-1 for the tenant.
        // Dashboard should mention the unit somewhere.
        body.Should().ContainAny("E2E Test Tower", "E2E-1", "Active",
            "Dashboard must surface lease + unit info");
    }

    [Fact]
    public async Task Logout_clears_session_and_redirects_to_login()
    {
        var (ctx, page) = await SignInAsync();
        await using var _ = ctx;

        await page.GotoAsync("/TenantPortal/Logout",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded });

        // After logout, accessing a protected page must redirect to login.
        await page.GotoAsync("/TenantPortal/Dashboard",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        page.Url.Should().Contain("/TenantPortal/Login",
            "Dashboard must redirect to Login once session is cleared");
    }
}
