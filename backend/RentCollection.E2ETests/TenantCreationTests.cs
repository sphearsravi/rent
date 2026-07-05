using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// Browser-tier coverage of the Tenant creation workflow (Sprint 15).
/// Tenant Civil ID (GovernmentId) is a uniqueness key in practice — the
/// happy path tests use a fresh Guid-suffix to avoid collisions across runs.
/// </summary>
public sealed class TenantCreationTests : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public TenantCreationTests(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Anonymous_request_to_Tenants_Create_redirects_to_login()
    {
        await using var ctx = await _fixture.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync("/Tenants/Create");
        page.Url.Should().Contain("/Account/Login");
        page.Url.Should().Contain("ReturnUrl");
    }

    [Fact]
    public async Task Form_renders_required_tenant_fields()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        await page.GotoAsync("/Tenants/Create");

        foreach (var id in new[] { "FirstName", "LastName", "civilIdInput",
                                   "BuildingPACINumber", "Email", "Phone" })
        {
            (await page.Locator($"#{id}").CountAsync()).Should().Be(1, $"#{id} is required on the form");
        }
    }

    [Fact]
    public async Task Empty_form_submit_shows_required_field_errors()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        await page.GotoAsync("/Tenants/Create");

        await page.GetByRole(AriaRole.Button, new() { Name = "Create" }).First.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        page.Url.Should().Contain("/Tenants/Create");
        (await page.ContentAsync()).Should().Contain("required");
    }

    // The tenant happy-path (valid submit → persisted → appears in listing) now
    // lives in TenantCreatePageObjectTests, driven through the TenantCreatePage
    // page object with DB verification + self-cleanup. The old inline helper
    // here used stale selectors (#PACINumber, #CompanyId) and was superseded.
}

internal static class StringContainsAnyExtension
{
    /// <summary>FluentAssertions doesn't have ContainAny for strings — shim it.</summary>
    public static AndConstraint<FluentAssertions.Primitives.StringAssertions> ContainAny(
        this FluentAssertions.Primitives.StringAssertions assertions, params string[] needles)
    {
        var subject = assertions.Subject ?? "";
        var hit = needles.Any(n => subject.Contains(n, StringComparison.OrdinalIgnoreCase));
        hit.Should().BeTrue($"expected one of [{string.Join(", ", needles)}] but found none in: {subject.Substring(0, Math.Min(200, subject.Length))}");
        return new AndConstraint<FluentAssertions.Primitives.StringAssertions>(assertions);
    }
}
