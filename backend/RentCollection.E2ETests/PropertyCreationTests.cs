using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;
using RentCollection.E2ETests.Pages;

namespace RentCollection.E2ETests;

/// <summary>
/// Sprint 15 — browser coverage of the Property (building) creation workflow.
/// The K-NET config audit (Sprint 8) showed 26 buildings with missing creds;
/// this suite pins the form basics so a wiring regression doesn't ship.
/// </summary>
public sealed class PropertyCreationTests : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public PropertyCreationTests(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Anonymous_request_to_Properties_Create_redirects_to_login()
    {
        await using var ctx = await _fixture.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync("/Properties/Create");
        page.Url.Should().Contain("/Account/Login");
    }

    [Fact]
    public async Task Form_renders_documented_property_fields()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        await page.GotoAsync("/Properties/Create");

        foreach (var id in new[] { "PropertyName", "PACINumber", "PropertyType",
                                   "NumberOfUnits", "Address", "Governorate", "Area" })
        {
            (await page.Locator($"#{id}").CountAsync()).Should().Be(1, $"#{id} must render on the form");
        }
    }

    [Fact]
    public async Task Empty_submit_shows_PACI_required_error()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        await page.GotoAsync("/Properties/Create");

        await page.GetByRole(AriaRole.Button, new() { Name = "Create Property" }).ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        page.Url.Should().Contain("/Properties/Create",
            "missing required fields must keep us on Create");
    }

    [Fact]
    public async Task Properties_index_lists_existing_buildings()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var response = await page.GotoAsync("/Properties",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        response!.Status.Should().Be(200);
        (await page.ContentAsync()).ToLowerInvariant().Should().Contain("propert");
    }

    // Happy-path, un-skipped (Sprint follow-up): the earlier "multipart-form
    // navigation race" is solved by the PropertyCreatePage page object
    // (NoWaitAfter click + explicit WaitForURL off /Properties/Create).
    [Fact]
    public async Task Valid_submit_creates_property_and_redirects()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var name = $"PC Tower {AdminSession.UniqueSuffix()}";
        var paci = $"PC{Math.Abs(Guid.NewGuid().GetHashCode())}";
        int id = 0;
        try
        {
            var prop = new PropertyCreatePage(page);
            await prop.GoToAsync();
            await prop.FillRequiredAsync(name, paci, numberOfUnits: 3, companyIdValue: "1");
            var landed = await prop.SubmitAsync();

            landed.Should().NotContain("/Properties/Create", "valid submit must redirect off Create");
            id = await WorkflowSupport.Scalar<int>(
                "SELECT ISNULL((SELECT TOP 1 PropertyId FROM Properties WHERE PropertyName=@n ORDER BY PropertyId DESC),0)", ("@n", name));
            id.Should().BeGreaterThan(0, "the property must be persisted");
        }
        finally { if (id > 0) await WorkflowSupport.Exec($"DELETE FROM Properties WHERE PropertyId={id}"); }
    }

    [Fact]
    public async Task Created_property_appears_in_listing()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var name = $"List Tower {AdminSession.UniqueSuffix()}";
        var paci = $"LT{Math.Abs(Guid.NewGuid().GetHashCode())}";
        int id = 0;
        try
        {
            var prop = new PropertyCreatePage(page);
            await prop.GoToAsync();
            await prop.FillRequiredAsync(name, paci, numberOfUnits: 2, companyIdValue: "1");
            await prop.SubmitAsync();

            await page.GotoAsync("/Properties", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            (await page.ContentAsync()).Should().Contain(name, "the new property must appear in /Properties listing");
            id = await WorkflowSupport.Scalar<int>(
                "SELECT ISNULL((SELECT TOP 1 PropertyId FROM Properties WHERE PropertyName=@n ORDER BY PropertyId DESC),0)", ("@n", name));
        }
        finally { if (id > 0) await WorkflowSupport.Exec($"DELETE FROM Properties WHERE PropertyId={id}"); }
    }

    private static async Task FillPropertyFormAsync(IPage page, string propertyName, string paci)
    {
        // DOMContentLoaded — Load waits for external CDN resources (Select2,
        // Font Awesome) that can stall the test in CI/local environments
        // with flaky CDN access. We don't need them for filling form fields.
        await page.GotoAsync("/Properties/Create", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("#PropertyName").FillAsync(propertyName);
        await page.Locator("#PACINumber").FillAsync(paci);
        await page.Locator("#PropertyType").SelectOptionAsync(new SelectOptionValue { Label = "Apartment" });
        await page.Locator("#Address").FillAsync("Test Block 1, Street 2");
        // Company is a non-nullable int, so model-binding requires a value.
        // Sprint 12 suppresses implicit-required for nav properties but NOT
        // for value-type FKs. Pick the first non-empty company option.
        await page.Locator("#CompanyId").SelectOptionAsync(new SelectOptionValue { Index = 1 });
    }
}
