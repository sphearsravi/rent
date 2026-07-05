using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// Sprint 17 — Bank transfer approval queue. The MEC step that tracks
/// "bank transfer approvals" (Sprint M-A step 6) reads its count from
/// this queue; if the page breaks, that aggregator silently mis-counts.
/// </summary>
public sealed class BankTransferApprovalTests : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public BankTransferApprovalTests(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Anonymous_request_to_PaymentApproval_redirects_to_login()
    {
        await using var ctx = await _fixture.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync("/PaymentApproval");
        page.Url.Should().Contain("/Account/Login");
    }

    [Fact]
    public async Task PaymentApproval_index_renders_for_admin()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var response = await page.GotoAsync("/PaymentApproval");
        response!.Status.Should().Be(200);
        (await page.ContentAsync()).ToLowerInvariant().Should().ContainAny("approval", "transfer", "pending");
    }
}
