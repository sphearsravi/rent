using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// Sprint 16 — invoice listing + detail page coverage. Invoice generation
/// happens via Hangfire jobs (unit-tested in <c>RecurringJobsTests</c>);
/// this suite covers the read paths.
/// </summary>
public sealed class InvoiceFlowTests : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public InvoiceFlowTests(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Anonymous_request_to_Invoices_redirects_to_login()
    {
        await using var ctx = await _fixture.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync("/Invoices");
        page.Url.Should().Contain("/Account/Login");
    }

    [Fact]
    public async Task Invoices_index_renders_list()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var response = await page.GotoAsync("/Invoices");
        response!.Status.Should().Be(200);
        (await page.ContentAsync()).ToLowerInvariant().Should().Contain("invoice");
    }

    [Fact]
    public async Task Invoices_index_can_filter_by_status()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;

        foreach (var status in new[] { "Pending", "Paid", "Overdue" })
        {
            var response = await page.GotoAsync($"/Invoices?status={status}");
            response!.Status.Should().Be(200, $"filter ?status={status} must not 500");
        }
    }

    [Fact]
    public async Task Invoice_details_404s_for_nonexistent_invoice()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var response = await page.GotoAsync("/Invoices/Details/99999999");
        response!.Status.Should().BeOneOf(new[] { 404 },
            "non-existent invoice must 404, not 500");
    }
}
