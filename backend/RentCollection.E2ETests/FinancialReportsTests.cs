using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// Read-only coverage for the five detailed FINANCIAL REPORTS pages and their
/// Excel/PDF exports. The Reports landing, snapshot JSON, and month-end-close
/// are exercised elsewhere (AdminJourneyTests / DashboardRenderingTests) — the
/// detailed report actions on <c>ReportsController</c> had no E2E coverage.
///
/// These tests only assert the reports render and the exports download for an
/// authenticated Financial-role admin against whatever data is seeded. No
/// workflow tree is built and nothing is torn down — the reports are pure reads.
///
/// Routes come straight from ReportsController.cs (all GET, optional
/// <c>?fromDate=&amp;toDate=</c>). Assertions are deliberately tolerant
/// (ContainAny / length &gt; 0) so they pass regardless of data volume.
/// </summary>
[Collection("WorkflowSerial")]
public sealed class FinancialReportsTests : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public FinancialReportsTests(E2EFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData("/Reports/ExecutiveDashboard")]
    [InlineData("/Reports/PropertyPerformance")]
    [InlineData("/Reports/TenantPaymentAnalysis")]
    [InlineData("/Reports/InvoiceStatus")]
    [InlineData("/Reports/PaymentReport")]
    public async Task Detailed_report_pages_render_for_admin(string route)
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;

        await page.GotoAsync(route, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        // An authenticated Financial admin must not be bounced to the login
        // form — landing back there means the [Authorize] gate or the seeded
        // role is wrong, not that the report is empty.
        page.Url.Should().NotContain("/Account/Login",
            "an authenticated Financial admin should reach the report, not the login form");

        // Every financial report renders at least one of these headline
        // terms. ContainAny keeps the test green whether the DB has one row
        // or ten thousand — we're pinning "the page rendered", not the numbers.
        var body = await page.ContentAsync();
        body.Should().ContainAny(
            "KWD", "Total", "Revenue", "Outstanding", "Collection", "Invoice", "Payment");
    }

    [Theory]
    [InlineData("/Reports/ExecutiveDashboardExcel")]
    [InlineData("/Reports/PropertyPerformanceExcel")]
    [InlineData("/Reports/TenantPaymentAnalysisExcel")]
    [InlineData("/Reports/InvoiceStatusExcel")]
    public async Task Excel_exports_return_a_spreadsheet(string route)
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;

        // APIRequest shares the context's auth cookie, so the download is
        // authenticated without driving the browser through a save dialog.
        var resp = await ctx.APIRequest.GetAsync(route);
        resp.Status.Should().Be(200);

        var contentType = (resp.Headers.TryGetValue("content-type", out var ct) ? ct : "")
            .ToLowerInvariant();
        contentType.Should().ContainAny("spreadsheet", "excel", "officedocument",
            "the export is an .xlsx workbook served as an OpenXML spreadsheet");

        (await resp.BodyAsync()).Length.Should().BeGreaterThan(0,
            "an empty body means the workbook never serialized");
    }

    [Fact]
    public async Task Executive_dashboard_pdf_downloads()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;

        var resp = await ctx.APIRequest.GetAsync("/Reports/ExecutiveDashboardPdf");
        resp.Status.Should().Be(200);

        var contentType = (resp.Headers.TryGetValue("content-type", out var ct) ? ct : "")
            .ToLowerInvariant();
        contentType.Should().Contain("pdf",
            "the executive dashboard export is served as application/pdf");

        (await resp.BodyAsync()).Length.Should().BeGreaterThan(0,
            "an empty body means QuestPDF never rendered the document");
    }

    [Fact]
    public async Task Reports_require_authentication()
    {
        // Fresh context — no sign-in — so we hold the anonymous cookie jar.
        await using var ctx = await _fixture.NewContextAsync();

        // MaxRedirects=0 stops us following the cookie-auth redirect so we can
        // observe the gate itself: a 3xx to /Account/Login, or a hard 401/403.
        // Financial reports must never be readable anonymously.
        var resp = await ctx.APIRequest.GetAsync("/Reports/ExecutiveDashboard",
            new APIRequestContextOptions { MaxRedirects = 0 });

        var status = resp.Status;
        var isRedirectToLogin = status >= 300 && status < 400
            && (resp.Headers.TryGetValue("location", out var loc)
                && loc.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase));
        var isForbidden = status is 401 or 403;

        (isRedirectToLogin || isForbidden).Should().BeTrue(
            $"an anonymous user must not read financial reports (got status {status})");
    }
}
