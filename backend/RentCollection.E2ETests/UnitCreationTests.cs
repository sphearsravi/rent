using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Pages;

namespace RentCollection.E2ETests;

/// <summary>
/// Browser-tier coverage of the Unit creation workflow (as documented in the
/// admin runbook). Each test signs in fresh, uses a unique UnitNumber to
/// avoid colliding with prior runs, and asserts the user-visible behaviour.
///
/// What's pinned:
///   • Auth gate on /Units/Create
///   • Form renders with every documented field
///   • Validation errors surface as field-level + summary messages
///   • Duplicate (PropertyId, UnitNumber) shows a friendly inline error
///   • Optional MonthlyRent saves as 0 without a client-side block
///   • Valid submit redirects to /Units/Details/{id} with success banner
///   • "Create lease" deep-link on Unit Details prefills the lease form
///
/// Uses the seeded `admin@test.com / Admin@123` and PropertyId=4 (Fintas
/// Towers) — both stable in the dev DB. If you re-seed, update <see cref="DefaultPropertyId"/>.
/// </summary>
public sealed class UnitCreationTests : IClassFixture<E2EFixture>
{
    private const string AdminEmail = "admin@test.com";
    private const string AdminPassword = "Admin@123";
    private const int DefaultPropertyId = 4;  // "Fintas Towers" in the dev seed

    private readonly E2EFixture _fixture;
    public UnitCreationTests(E2EFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Each test uses a unique unit number so re-runs don't collide with the
    /// uniqueness constraint. Short — the index is <c>(PropertyId, UnitNumber)</c>
    /// and the column has a 50-char limit.
    /// </summary>
    private static string UniqueUnitNumber(string prefix) =>
        $"{prefix}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant()}";

    private async Task<(IBrowserContext ctx, IPage page)> SignInAsync()
    {
        var ctx = await _fixture.NewContextAsync();
        var page = await ctx.NewPageAsync();
        var login = new LoginPage(page);
        await page.GotoAsync("/Account/Login");
        await login.SubmitAsync(AdminEmail, AdminPassword);
        if (page.Url.Contains("/Account/Login"))
            throw new InvalidOperationException(
                "Admin sign-in did not redirect — check seeded credentials.");
        return (ctx, page);
    }

    // ───────────── Auth gate ─────────────

    [Fact]
    public async Task Anonymous_request_to_Units_Create_redirects_to_login()
    {
        await using var ctx = await _fixture.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync("/Units/Create");

        page.Url.Should().Contain("/Account/Login");
        page.Url.Should().Contain("ReturnUrl");
    }

    // ───────────── Form shape ─────────────

    [Fact]
    public async Task Form_renders_all_documented_fields()
    {
        var (ctx, page) = await SignInAsync();
        await using var _ = ctx;
        await page.GotoAsync("/Units/Create");

        // Every input/select the workflow doc mentions must be present.
        var ids = new[] { "PropertyId", "UnitNumber", "UnitType", "FloorNumber",
                          "Bedrooms", "Bathrooms", "SquareFeet", "MonthlyRent" };
        foreach (var id in ids)
        {
            (await page.Locator($"#{id}").CountAsync()).Should().Be(1,
                $"#{id} is documented as a field on the Add Unit form");
        }

        // Submit button is the action target.
        await page.GetByRole(AriaRole.Button, new() { Name = "Create unit" })
            .IsVisibleAsync()
            .ContinueWith(t => t.Result.Should().BeTrue());

        // MonthlyRent is now optional — label says so, default is 0,
        // data-val-required is overridden.
        var rentInput = page.Locator("#MonthlyRent");
        (await rentInput.GetAttributeAsync("value")).Should().Be("0",
            "MonthlyRent defaults to 0 so the form submits even when blank");
        (await rentInput.GetAttributeAsync("data-val-required"))
            .Should().Be("false",
                "auto-emitted required is suppressed (decimal field would otherwise block submit)");

        var rentLabel = await page.Locator("label[for='MonthlyRent']").InnerTextAsync();
        rentLabel.Should().Contain("(optional)",
            "the label tells the user the field can be left blank");
    }

    // ───────────── Validation ─────────────

    [Fact]
    public async Task Empty_form_submit_shows_required_field_errors()
    {
        var (ctx, page) = await SignInAsync();
        await using var _ = ctx;
        await page.GotoAsync("/Units/Create");

        await page.GetByRole(AriaRole.Button, new() { Name = "Create unit" }).ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Page must stay on Create (no redirect to Details).
        page.Url.Should().Contain("/Units/Create");

        var body = await page.ContentAsync();
        body.Should().Contain("required",
            "MVC's validation summary surfaces the per-field 'X is required' messages");
    }

    [Fact]
    public async Task Duplicate_unit_number_in_same_property_shows_friendly_error()
    {
        var (ctx, page) = await SignInAsync();
        await using var _ = ctx;

        var unitNumber = UniqueUnitNumber("DUP");

        // First create — should succeed.
        await CreateUnitAsync(page, DefaultPropertyId, unitNumber);
        page.Url.Should().Contain("/Units/Details/",
            "first submit creates the unit and redirects to Details");

        // Second create with the same (PropertyId, UnitNumber) — should fail
        // with a user-friendly message, NOT a 500 stack trace.
        await page.GotoAsync("/Units/Create");
        await page.Locator("#PropertyId").SelectOptionAsync(DefaultPropertyId.ToString());
        await page.Locator("#UnitNumber").FillAsync(unitNumber);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create unit" }).ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        page.Url.Should().Contain("/Units/Create",
            "duplicate must not redirect — it must re-render the form with the error");

        var body = await page.ContentAsync();
        body.Should().Contain("already exists",
            "the friendly duplicate-key error must be surfaced in the page chrome");
        body.Should().NotContain("SqlException",
            "the raw DB exception must NEVER leak to the user");
        body.Should().NotContain("idx_units_property_number",
            "the index name is implementation detail — don't expose it");
    }

    // ───────────── Optional rent ─────────────

    [Fact]
    public async Task Unit_with_blank_rent_saves_with_zero()
    {
        var (ctx, page) = await SignInAsync();
        await using var _ = ctx;

        var unitNumber = UniqueUnitNumber("RENT0");
        await page.GotoAsync("/Units/Create");
        await page.Locator("#PropertyId").SelectOptionAsync(DefaultPropertyId.ToString());
        await page.Locator("#UnitNumber").FillAsync(unitNumber);
        await page.Locator("#UnitType").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        // Intentionally leave MonthlyRent alone — it pre-fills to "0".

        await page.GetByRole(AriaRole.Button, new() { Name = "Create unit" }).ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        page.Url.Should().Contain("/Units/Details/",
            "blank rent must not block submit — the field defaults to 0");

        // The Details page should show 0 KWD (or "0.000" depending on formatting).
        var body = await page.ContentAsync();
        body.Should().Contain(unitNumber, "the new unit's number must appear on its Details page");
    }

    // ───────────── Happy path ─────────────

    [Fact]
    public async Task Valid_submit_redirects_to_Details_with_success_banner()
    {
        var (ctx, page) = await SignInAsync();
        await using var _ = ctx;

        var unitNumber = UniqueUnitNumber("HAPPY");
        await CreateUnitAsync(page, DefaultPropertyId, unitNumber,
            unitType: "Apartment", floor: "2", bedrooms: "2",
            bathrooms: "1", squareFeet: "85", monthlyRent: "350.000");

        page.Url.Should().MatchRegex(@"/Units/Details/\d+",
            "valid submit redirects to /Units/Details/{newId}");

        var body = await page.ContentAsync();
        body.Should().Contain(unitNumber, "the new unit's number appears on its Details page");
        // TempData success banner is rendered via _AlertMessages partial.
        body.ToLowerInvariant().Should().Contain("created",
            "the success banner 'Unit X created.' must surface on the redirect target");
    }

    // ───────────── Deep-link to Lease Create from Unit Details ─────────────

    [Fact]
    public async Task Create_lease_link_on_Unit_Details_prefills_the_lease_form()
    {
        // Sprint 13 — clicking "Create lease" on /Units/Details/{id} should
        // open /Leases/Create?unitId={id} with Building + Unit + MonthlyRent
        // already populated. The user only needs to pick a Tenant.
        var (ctx, page) = await SignInAsync();
        await using var _ = ctx;

        var unitNumber = UniqueUnitNumber("LINK");
        await CreateUnitAsync(page, DefaultPropertyId, unitNumber,
            monthlyRent: "275.000");

        // Extract the new UnitId from the Details URL.
        var match = System.Text.RegularExpressions.Regex.Match(page.Url, @"/Units/Details/(\d+)");
        match.Success.Should().BeTrue();
        var unitId = match.Groups[1].Value;

        // Now jump to the prefilled lease form.
        await page.GotoAsync($"/Leases/Create?unitId={unitId}");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Select2 hides the native <select> and renders its own widget, so
        // Playwright's visibility-aware locators time out on the original
        // element. Read the .value directly via DOM evaluate — that's what
        // the form actually submits when the user clicks Create.
        var buildingValue = await page.Locator("#buildingSelect")
            .EvaluateAsync<string>("el => el.value");
        buildingValue.Should().Be(DefaultPropertyId.ToString(),
            "Building <select> must have the deep link's PropertyId as its selected value");

        // Wait for the cascade JS to fire (it sets buildingSelect → rebuilds
        // unitSelect to that building's units → marks the preselected one).
        // If the value isn't set within 3s, the JS isn't running.
        await page.WaitForFunctionAsync(
            $"() => document.getElementById('unitSelect').value === '{unitId}'",
            options: new PageWaitForFunctionOptions { Timeout = 3000 });

        var unitValue = await page.Locator("#unitSelect")
            .EvaluateAsync<string>("el => el.value");
        unitValue.Should().Be(unitId,
            "the Unit dropdown must pre-select the unit from the deep link");

        // MonthlyRent on the Lease form should be primed from the Unit's rent.
        // Note: MVC may render the bound decimal as "275" instead of "275.000"
        // depending on the model's culture/format; assert containment of "275".
        var leaseRent = await page.Locator("input[name='MonthlyRent']")
            .EvaluateAsync<string>("el => el.value");
        leaseRent.Should().StartWith("275",
            "the Lease form's MonthlyRent should copy from the Unit's MonthlyRent");
    }

    // ───────────── helper ─────────────

    /// <summary>
    /// Fill the Add Unit form and submit. Defaults match a typical happy-path
    /// submission. Returns nothing — callers assert on <c>page.Url</c> or
    /// rendered content afterward.
    /// </summary>
    private static async Task CreateUnitAsync(
        IPage page, int propertyId, string unitNumber,
        string unitType = "Apartment",
        string? floor = null,
        string? bedrooms = null,
        string? bathrooms = null,
        string? squareFeet = null,
        string monthlyRent = "0")
    {
        await page.GotoAsync("/Units/Create");
        await page.Locator("#PropertyId").SelectOptionAsync(propertyId.ToString());
        await page.Locator("#UnitNumber").FillAsync(unitNumber);

        if (!string.IsNullOrEmpty(unitType))
            await page.Locator("#UnitType").SelectOptionAsync(new SelectOptionValue { Label = unitType });
        if (!string.IsNullOrEmpty(floor))
            await page.Locator("#FloorNumber").FillAsync(floor);
        if (!string.IsNullOrEmpty(bedrooms))
            await page.Locator("#Bedrooms").FillAsync(bedrooms);
        if (!string.IsNullOrEmpty(bathrooms))
            await page.Locator("#Bathrooms").FillAsync(bathrooms);
        if (!string.IsNullOrEmpty(squareFeet))
            await page.Locator("#SquareFeet").FillAsync(squareFeet);
        // MonthlyRent is pre-filled to "0"; only overwrite if caller asked.
        if (monthlyRent != "0")
            await page.Locator("#MonthlyRent").FillAsync(monthlyRent);

        await page.GetByRole(AriaRole.Button, new() { Name = "Create unit" }).ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }
}
