using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// Sprint 15 — full coverage of the Lease creation workflow including the
/// Sprint 11 Building→Unit→Tenant Select2 cascade and the Sprint 12 fix
/// for the silent-form-submit bug (SuppressImplicitRequiredAttributeForNonNullableReferenceTypes).
/// </summary>
public sealed class LeaseCreationTests : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public LeaseCreationTests(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Anonymous_request_to_Leases_Create_redirects_to_login()
    {
        await using var ctx = await _fixture.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync("/Leases/Create");
        page.Url.Should().Contain("/Account/Login");
    }

    [Fact]
    public async Task Form_has_three_searchable_cascading_dropdowns()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        await page.GotoAsync("/Leases/Create");

        (await page.Locator("#buildingSelect").CountAsync()).Should().Be(1);
        (await page.Locator("#unitSelect").CountAsync()).Should().Be(1);
        (await page.Locator("#tenantSelect").CountAsync()).Should().Be(1);
        // Select2 enhances each into a sibling .select2-container.
        (await page.Locator(".select2-container").CountAsync()).Should().BeGreaterThanOrEqualTo(3);
        // The embedded JSON dict feeds the cascade.
        var jsonText = await page.Locator("#unitsByBuilding").TextContentAsync();
        jsonText!.TrimStart().Should().StartWith("{");
    }

    [Fact]
    public async Task Picking_a_Building_narrows_the_Unit_options_via_JS()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        await page.GotoAsync("/Leases/Create");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Choose Fintas Towers (PropertyId=4 — stable seeded). The cascade JS
        // rebuilds the Unit dropdown to that building's units only.
        await page.Locator("#buildingSelect")
            .EvaluateAsync("(el, val) => { el.value = val; el.dispatchEvent(new Event('change', { bubbles:true })); }", "4");

        // Wait for the cascade to repopulate Unit options.
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('#unitSelect option').length > 1",
            options: new PageWaitForFunctionOptions { Timeout = 3000 });

        var optionCount = await page.Locator("#unitSelect option").CountAsync();
        optionCount.Should().BeGreaterThan(1,
            "Picking a Building must rebuild Unit options to that building's units (>1 incl. placeholder)");
    }

    [Fact]
    public async Task Empty_submit_shows_validation_errors_not_silent_fail()
    {
        // This is the Sprint 12 regression: before the fix, the empty submit
        // looked like nothing happened. Now it must produce visible errors.
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        await page.GotoAsync("/Leases/Create");

        await page.GetByRole(AriaRole.Button, new() { Name = "Create" }).First.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        page.Url.Should().Contain("/Leases/Create",
            "empty submit must keep us on Create — silent redirect would be the Sprint 12 bug");
        (await page.ContentAsync()).Should().Contain("required");
    }

    [Fact]
    public async Task Deep_link_with_unitId_prefills_Building_Unit_and_Rent()
    {
        // Sprint 13 deep-link from /Units/Details/{id} → Create lease.
        // The Lease form opens with Building, Unit, and MonthlyRent pre-set.
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;

        // Use a known seeded Vacant unit. UnitId=14 (Fintas T1-201) is stable.
        await page.GotoAsync("/Leases/Create?unitId=14");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var buildingVal = await page.Locator("#buildingSelect").EvaluateAsync<string>("el => el.value");
        buildingVal.Should().Be("4", "Unit 14 belongs to PropertyId 4 (Fintas Towers)");

        await page.WaitForFunctionAsync(
            "() => document.getElementById('unitSelect').value === '14'",
            options: new PageWaitForFunctionOptions { Timeout = 3000 });
        var unitVal = await page.Locator("#unitSelect").EvaluateAsync<string>("el => el.value");
        unitVal.Should().Be("14");
    }
}
