using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// Sprint 18 — view-only dashboard + report rendering. Pins the obvious
/// "page loads and returns 200" for the dashboards admins live in daily.
/// </summary>
public sealed class DashboardRenderingTests : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public DashboardRenderingTests(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Dashboard_renders_for_admin()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var response = await page.GotoAsync("/Dashboard");
        response!.Status.Should().Be(200);
    }

    [Fact]
    public async Task Reports_snapshot_endpoint_returns_well_shaped_json()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var response = await page.GotoAsync("/Reports/Snapshot");
        response!.Status.Should().Be(200);
        var body = await response.TextAsync();
        body.Should().StartWith("{",
            "snapshot is a JSON object — first non-whitespace char must be '{'");
    }

    [Fact]
    public async Task Reports_index_renders_with_tiles()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var response = await page.GotoAsync("/Reports");
        response!.Status.Should().Be(200);
        (await page.ContentAsync()).ToLowerInvariant().Should().Contain("report");
    }

    [Fact]
    public async Task Leases_KpiBatch_returns_200()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var response = await page.GotoAsync("/Leases/KpiBatch");
        response!.Status.Should().Be(200);
    }
}
