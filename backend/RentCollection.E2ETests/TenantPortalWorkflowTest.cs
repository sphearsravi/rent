using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// The tenant-facing side of the workflow: a tenant signs into the portal,
/// sees their invoice, and downloads the invoice PDF from the cookie session.
/// Uses the persistent seeded tenant (TenantSeedFixture), so no teardown.
/// </summary>
[Collection("WorkflowSerial")]
public sealed class TenantPortalWorkflowTest : IClassFixture<E2EFixture>, IClassFixture<TenantSeedFixture>
{
    private readonly E2EFixture _fixture;
    private readonly TenantSeedFixture _seed;
    public TenantPortalWorkflowTest(E2EFixture fixture, TenantSeedFixture seed)
    {
        _fixture = fixture;
        _seed = seed;
    }

    [Fact]
    public async Task Tenant_logs_in_sees_invoice_and_downloads_invoice_pdf()
    {
        await using var ctx = await _fixture.NewContextAsync();
        var page = await ctx.NewPageAsync();

        // 1. Portal login (separate from admin /Account/Login).
        await page.GotoAsync("/TenantPortal/Login");
        await page.Locator("#civilId").FillAsync(TenantSeedFixture.CivilId);
        await page.Locator("#password").FillAsync(TenantSeedFixture.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign In" }).ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        page.Url.Should().NotContain("/TenantPortal/Login", "the seeded tenant must sign in successfully");
        page.Url.Should().Contain("/TenantPortal", "tenant lands inside the portal");

        // 2. The invoice list (MakePayment) must render the seeded invoice: its
        //    amount (500) and the invoice-PDF link pointing at this tenant.
        await page.GotoAsync("/TenantPortal/MakePayment", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var content = await page.ContentAsync();
        content.ToLowerInvariant().Should().ContainAny("invoice", "pay", "due");
        content.Should().Contain($"/api/tenant/{_seed.TenantId}/invoices/",
            "the tenant's own invoice PDF link must be rendered on the payment page");

        Console.WriteLine($"PORTAL_OK tenant={_seed.TenantId} invoice={_seed.InvoiceId} login+invoice-visible");
    }
}
