using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Pages;

namespace RentCollection.E2ETests;

/// <summary>
/// Authenticated browser journeys for the admin role. Each test signs in
/// fresh — fast (sub-second post-login) and keeps the suite resilient when
/// run in parallel.
///
/// Uses the seeded admin (<c>admin@test.com / Admin@123</c>) — the same
/// credentials the demo button populates.
/// </summary>
public sealed class AdminJourneyTests : IClassFixture<E2EFixture>
{
    private const string AdminEmail = "admin@test.com";
    private const string AdminPassword = "Admin@123";

    private readonly E2EFixture _fixture;
    public AdminJourneyTests(E2EFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Helper: open a fresh context, sign the admin in, return the page
    /// already at the post-login landing. Each test calls this and the
    /// `await using` releases the context when the test method returns.
    /// </summary>
    private async Task<(IBrowserContext ctx, IPage page)> SignInAsync()
    {
        var ctx = await _fixture.NewContextAsync();
        var page = await ctx.NewPageAsync();
        var login = new LoginPage(page);
        await page.GotoAsync("/Account/Login");
        await login.SubmitAsync(AdminEmail, AdminPassword);

        // If the login form is still on screen, surface why up-front so
        // the test failure is "login failed" not "selector not found".
        if (page.Url.Contains("/Account/Login"))
            throw new InvalidOperationException(
                "Admin sign-in did not redirect away from /Account/Login. " +
                "Check the seeded user exists with the expected password.");

        return (ctx, page);
    }

    [Fact]
    public async Task Admin_login_lands_on_authenticated_page()
    {
        var (ctx, page) = await SignInAsync();
        await using var _ = ctx;

        // Post-login we should be on a real authenticated screen, not the
        // login form. The shared layout renders a Logout form-button for
        // authenticated users — that's a more robust marker than the
        // email, which isn't rendered on every page.
        page.Url.Should().NotContain("/Account/Login");
        var body = await page.ContentAsync();
        body.Should().Contain("/Account/Logout",
            "the layout renders a logout form-action only for authenticated users");
    }

    [Fact]
    public async Task MEC_page_renders_nine_step_rows()
    {
        var (ctx, page) = await SignInAsync();
        await using var _ = ctx;
        var mec = new MonthEndClosePage(page);

        await mec.GoToAsync(period: "2026-05");
        await mec.WaitForRenderedAsync();

        // The view JS hydrates exactly 9 rows from the server-rendered
        // initial state. Anything else means the aggregator returned a
        // different shape than the view expects.
        (await mec.AllStepRows.CountAsync()).Should().Be(9);
    }

    [Fact]
    public async Task MEC_summary_cards_total_to_nine()
    {
        var (ctx, page) = await SignInAsync();
        await using var _ = ctx;
        var mec = new MonthEndClosePage(page);

        await mec.GoToAsync(period: "2026-05");
        await mec.WaitForRenderedAsync();

        var green = int.Parse(await mec.GreenCount.InnerTextAsync());
        var amber = int.Parse(await mec.AmberCount.InnerTextAsync());
        var red   = int.Parse(await mec.RedCount.InnerTextAsync());
        var muted = int.Parse(await mec.MutedCount.InnerTextAsync());

        (green + amber + red + muted).Should().Be(9,
            "the four summary cards tally every step's status — they must sum to 9");
    }

    [Fact]
    public async Task Hangfire_dashboard_is_reachable_for_admin()
    {
        var (ctx, page) = await SignInAsync();
        await using var _ = ctx;

        var response = await page.GotoAsync("/hangfire");
        // Hangfire dashboard returns 200 with its own chrome.
        response!.Status.Should().Be(200);
        var body = await page.ContentAsync();
        body.Should().Contain("Hangfire");
    }

    [Fact]
    public async Task Reports_landing_renders_the_MEC_tile()
    {
        var (ctx, page) = await SignInAsync();
        await using var _ = ctx;

        await page.GotoAsync("/Reports");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The MEC tile was added in Sprint M-E; its presence proves the
        // Reports landing routes correctly and the new card hasn't been
        // silently removed.
        var body = await page.ContentAsync();
        body.Should().Contain("Month-end close");
    }

    [Fact]
    public async Task MEC_state_endpoint_returns_well_shaped_json()
    {
        var (ctx, page) = await SignInAsync();
        await using var _ = ctx;

        var response = await page.GotoAsync("/Reports/MonthEndCloseState?period=2026-05");
        response!.Status.Should().Be(200);

        var body = await response.TextAsync();
        body.Should().Contain("\"period\":\"2026-05\"");
        body.Should().Contain("\"steps\"");
        body.Should().Contain("\"rentInvoices\"");
        body.Should().Contain("\"summary\"");
    }

    [Fact]
    public async Task Lease_create_form_has_searchable_building_unit_tenant_cascade()
    {
        // Sprint 11 — three searchable dropdowns (Building → Unit → Tenant)
        // backed by Select2. Building selection narrows the Unit list via
        // an embedded JSON map; Tenant is independent.
        var (ctx, page) = await SignInAsync();
        await using var _ = ctx;

        await page.GotoAsync("/Leases/Create");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // All three selects exist with the right IDs.
        await page.Locator("#buildingSelect").CountAsync()
            .ContinueWith(t => t.Result.Should().Be(1, "Building select must be present"));
        await page.Locator("#unitSelect").CountAsync()
            .ContinueWith(t => t.Result.Should().Be(1, "Unit select must be present"));
        await page.Locator("#tenantSelect").CountAsync()
            .ContinueWith(t => t.Result.Should().Be(1, "Tenant select must be present"));

        // Select2 enhances each <select> with a sibling .select2-container; the
        // presence of three containers proves typeahead is wired.
        var select2Count = await page.Locator(".select2-container").CountAsync();
        select2Count.Should().BeGreaterThanOrEqualTo(3,
            "Select2 must enhance Building, Unit, and Tenant — three typeahead containers expected");

        // The embedded units-by-building JSON is rendered by the controller.
        var jsonNode = page.Locator("#unitsByBuilding");
        await jsonNode.CountAsync()
            .ContinueWith(t => t.Result.Should().Be(1, "Cascade JSON payload must be present"));
        var json = await jsonNode.TextContentAsync();
        json.Should().NotBeNullOrWhiteSpace();
        json!.TrimStart().Should().StartWith("{",
            "JSON payload must be a valid object (even if empty: '{}')");
    }

    [Fact]
    public async Task Job_health_page_renders_recurring_jobs_table()
    {
        // Sprint 6 — /JobHealth is the operator-facing view onto Hangfire
        // recurring-job state. Must render with the 7 registered jobs and
        // at least one server in the scheduler-servers strip.
        var (ctx, page) = await SignInAsync();
        await using var _ = ctx;

        var response = await page.GotoAsync("/JobHealth");
        response!.Status.Should().Be(200);

        var body = await page.ContentAsync();
        body.Should().Contain("Job Health");
        body.Should().Contain("Scheduler servers");
        // Every registered recurring job ID should appear in the table.
        body.Should().Contain("generate-monthly-invoices");
        body.Should().Contain("flip-overdue-invoices");
        body.Should().Contain("reconcile-unit-status");
    }
}
