using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;
using RentCollection.E2ETests.Pages;
using RentCollection.Infrastructure.Data;

namespace RentCollection.E2ETests;

/// <summary>
/// Tests for /Tenants/Create driven through the <see cref="TenantCreatePage"/>
/// page object. Keeping the selectors in the page object means these read as
/// intent, and a markup change is a one-file fix.
/// </summary>
public sealed class TenantCreatePageObjectTests : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public TenantCreatePageObjectTests(E2EFixture fixture) => _fixture = fixture;

    private static string ConnString =>
        Environment.GetEnvironmentVariable("RC_E2E_DB")
        ?? @"Server=RAVIWST\SQLEXPRESS;Database=RentCollectionDB;Trusted_Connection=true;TrustServerCertificate=true;";

    private static string TwelveDigits() =>
        Math.Abs(Guid.NewGuid().GetHashCode()).ToString().PadLeft(12, '0').Substring(0, 12);

    [Fact]
    public async Task Renders_required_essentials_and_action_buttons()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var t = new TenantCreatePage(page);
        await t.GoToAsync();

        (await t.FirstName.CountAsync()).Should().Be(1);
        (await t.LastName.CountAsync()).Should().Be(1);
        (await t.Email.CountAsync()).Should().Be(1);
        (await t.Phone.CountAsync()).Should().Be(1);
        (await t.Paci.CountAsync()).Should().Be(1);
        (await t.BuildingPaci.CountAsync()).Should().Be(1);
        (await t.CreateButton.CountAsync()).Should().Be(1);
        (await t.ClearAllButton.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Google_Gemini_scan_option_is_absent()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var t = new TenantCreatePage(page);
        await t.GoToAsync();

        (await t.ScanCardButton.CountAsync()).Should().Be(0, "the Gemini Scan card button was removed");
        (await t.GeminiConsentCheckbox.CountAsync()).Should().Be(0, "the Gemini consent checkbox was removed");
        (await t.CivilIdImageFile.CountAsync()).Should().Be(0, "the Gemini image upload was removed");
        (await page.ContentAsync()).Should().NotContain("Gemini");
    }

    [Fact]
    public async Task Clear_All_wipes_the_form()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var t = new TenantCreatePage(page);
        await t.GoToAsync();

        await t.FirstName.FillAsync("WipeMe");
        await t.Email.FillAsync("wipe@example.com");
        await t.Paci.FillAsync(TwelveDigits());
        await t.ClickClearAllAsync();

        (await t.FirstName.InputValueAsync()).Should().BeEmpty();
        (await t.Email.InputValueAsync()).Should().BeEmpty();
        (await t.Paci.InputValueAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Valid_submit_creates_tenant_persists_and_lists_then_self_cleans()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var t = new TenantCreatePage(page);
        await t.GoToAsync();

        var suffix = AdminSession.UniqueSuffix();
        var firstName = "PageObj";
        var lastName = $"T{suffix}";

        await t.FillEssentialsAsync(firstName, lastName, $"po+{suffix}@example.com",
            "99000555", TwelveDigits(), "PACI-FX-001");

        (await t.CheckHtml5ValidityAsync()).Should().Be("VALID");
        var landed = await t.SubmitAsync();
        landed.Should().NotContain("/Tenants/Create", "valid submit must redirect off Create");

        var opts = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnString).Options;
        int dbId = 0;
        await using (var db = new ApplicationDbContext(opts))
        {
            var row = await db.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(x => x.LastName == lastName && x.FirstName == firstName);
            if (row != null) dbId = row.TenantId;
        }

        await page.GotoAsync("/Tenants", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var listed = (await page.ContentAsync()).Contains(lastName);

        try
        {
            dbId.Should().BeGreaterThan(0, "the tenant must be persisted");
            listed.Should().BeTrue("the tenant must appear in the listing");
        }
        finally
        {
            if (dbId > 0)
            {
                await using var db = new ApplicationDbContext(opts);
                await db.Tenants.Where(x => x.TenantId == dbId).ExecuteDeleteAsync();
            }
        }
    }
}
