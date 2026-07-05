using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// Sprint 18 — Audit log viewer pages. Audit history is the operator's
/// only way to answer "who changed what when" after the fact, so the
/// pages must render reliably.
/// </summary>
public sealed class AuditLogViewerTests : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public AuditLogViewerTests(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Anonymous_request_to_Audit_redirects_to_login()
    {
        await using var ctx = await _fixture.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync("/Audit");
        page.Url.Should().Contain("/Account/Login");
    }

    [Fact]
    public async Task Audit_index_renders_for_admin()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var response = await page.GotoAsync("/Audit");
        response!.Status.Should().Be(200);
        (await page.ContentAsync()).ToLowerInvariant().Should().Contain("audit");
    }

    [Fact]
    public async Task Audit_entity_view_renders_or_404_no_500()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        // Probe a known entity type/id. Either renders (200) or graceful 404 —
        // never a 500 stack trace.
        var response = await page.GotoAsync("/Audit/Lease/678");
        response!.Status.Should().BeOneOf(new[] { 200, 404 });
    }
}
