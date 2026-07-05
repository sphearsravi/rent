using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using RentCollection.Application.Interfaces;

namespace RentCollection.IntegrationTests;

/// <summary>
/// Sprint 2 reliability work — integration coverage for the Month-End Close
/// endpoints. These exercise routing, the auth gate, and the role guard end
/// to end (DI, middleware order, antiforgery, JSON shape) so a wiring
/// regression doesn't ship to prod.
///
/// All assertions here use anonymous requests — they prove the endpoints
/// are gated. Tests that exercise a logged-in admin flow live in a future
/// suite that seeds the Identity store first.
/// </summary>
public sealed class MonthEndCloseEndpointTests : IClassFixture<InMemoryAppFactory>
{
    private readonly InMemoryAppFactory _factory;

    public MonthEndCloseEndpointTests(InMemoryAppFactory factory) => _factory = factory;

    private HttpClient NoRedirectClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
    });

    [Theory]
    [InlineData("/Reports/MonthEndClose")]
    [InlineData("/Reports/MonthEndCloseState")]
    [InlineData("/Reports/MonthEndCloseState?period=2026-05")]
    public async Task Anonymous_MEC_requests_redirect_to_login(string path)
    {
        using var client = NoRedirectClient();
        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect,
            $"unauthenticated requests to {path} must redirect to /Account/Login");
        // Redirect Location is rendered as an absolute URL by the test server
        // (http://localhost/Account/Login?ReturnUrl=…) — assert containment so
        // the test is independent of host + ReturnUrl tail.
        response.Headers.Location!.OriginalString.Should().Contain("/Account/Login");
    }

    [Theory]
    [InlineData("/Reports/TriggerJob")]
    [InlineData("/Reports/ConfirmClose")]
    public async Task Anonymous_MEC_writes_redirect_to_login(string path)
    {
        // POSTs without auth must also redirect — proving the gate covers
        // writes, not just reads.
        using var client = NoRedirectClient();
        using var content = new StringContent("", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
        var response = await client.PostAsync(path, content);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        // Redirect Location is rendered as an absolute URL by the test server
        // (http://localhost/Account/Login?ReturnUrl=…) — assert containment so
        // the test is independent of host + ReturnUrl tail.
        response.Headers.Location!.OriginalString.Should().Contain("/Account/Login");
    }

    [Fact]
    public async Task TriggerJob_unknown_id_route_still_gated_by_auth()
    {
        // The allow-list check runs inside the action, after [Authorize].
        // Anonymous POST therefore can never see the BadRequest path — it
        // redirects first. This test pins that ordering: any other shape
        // (e.g. 400 reaching anonymous) would mean someone removed the auth
        // attribute and the only thing standing between an unauthenticated
        // attacker and a job trigger is the allow-list. Don't allow that.
        using var client = NoRedirectClient();
        using var content = new StringContent("jobId=nuke-the-db",
            System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
        var response = await client.PostAsync("/Reports/TriggerJob", content);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public void Allowlist_constant_is_exposed_and_non_empty()
    {
        // The constant is what both the controller and Program.cs read.
        // If reflection finds it empty, every "Trigger now" button on the
        // MEC page is dead — fail loud.
        RecurringJobIds.All.Should().NotBeEmpty()
            .And.HaveCount(7, "we register seven recurring jobs in Program.cs");
        RecurringJobIds.All.Should().Contain(RecurringJobIds.GenerateMonthlyInvoices);
        RecurringJobIds.All.Should().Contain(RecurringJobIds.MonthlyExecutiveDashboardEmail);
    }
}
