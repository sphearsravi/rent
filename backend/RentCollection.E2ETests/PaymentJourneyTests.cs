using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// Sprint 7 Tier 5 — browser-tier coverage for the payment journey. Because
/// a real K-NET sandbox capture requires manual card entry, we cover what
/// CAN be automated: the portal navigation, the payment-failed redirect
/// shape, and the success/failed page rendering.
///
/// The sandbox-card flow itself lives in the <see cref="QA_RELEASE.md"/>
/// release runbook — a checklist a human walks through pre-release.
/// </summary>
public sealed class PaymentJourneyTests
    : IClassFixture<E2EFixture>, IClassFixture<TenantSeedFixture>
{
    private readonly E2EFixture _e2e;
    private readonly TenantSeedFixture _tenant;

    public PaymentJourneyTests(E2EFixture e2e, TenantSeedFixture tenant)
    {
        _e2e = e2e;
        _tenant = tenant;
    }

    private async Task<(IBrowserContext ctx, IPage page)> SignInTenantAsync()
    {
        var ctx = await _e2e.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync("/TenantPortal/Login");
        await page.Locator("#civilId").FillAsync(TenantSeedFixture.CivilId);
        await page.Locator("#password").FillAsync(TenantSeedFixture.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign In" }).ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        return (ctx, page);
    }

    [Fact]
    public async Task Payment_failed_page_renders_with_supplied_error_text()
    {
        // The KNET response handler redirects to /TenantPortal/PaymentFailed
        // with an `error` query param when the gateway returns anything but
        // CAPTURED. The page MUST render that error so the tenant knows
        // what to do next.
        await using var ctx = await _e2e.NewContextAsync();
        var page = await ctx.NewPageAsync();

        await page.GotoAsync("/TenantPortal/PaymentFailed?error=Card%20was%20declined&reference=TRK_E2E");
        var body = await page.ContentAsync();

        body.ToLowerInvariant().Should().Contain("card was declined",
            "the failed page must surface the error text from the URL");
    }

    [Fact]
    public async Task Payment_success_page_is_reachable_with_query_params()
    {
        // The companion page to PaymentFailed — KNET capture redirects here.
        // We don't fake a real CAPTURED flow (that requires the gateway);
        // we just prove the page renders when given query params.
        await using var ctx = await _e2e.NewContextAsync();
        var page = await ctx.NewPageAsync();

        var response = await page.GotoAsync("/TenantPortal/PaymentSuccess?paymentId=42&reference=RCPT-00000042");

        response!.Status.Should().Be(200);
        var body = await page.ContentAsync();
        body.ToLowerInvariant().Should().Contain("success",
            "the success page should render success-themed chrome");
    }

    // NOTE: a forged-callback test was originally drafted here but it
    // duplicates `KnetPaymentEndpointTests.Response_endpoint_with_unknown_trackid_redirects_to_failed_page`
    // which exercises the same behaviour through WebApplicationFactory without
    // a real browser. Keep the integration version — it's faster and the
    // signal is identical.
}
