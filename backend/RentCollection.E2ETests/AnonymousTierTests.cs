using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Pages;

namespace RentCollection.E2ETests;

/// <summary>
/// Browser-tier coverage of the public surface — pages anyone can hit
/// without signing in. These are the highest-impact regressions to catch
/// because a broken login page locks every user out simultaneously.
///
/// Each test owns its own <see cref="IBrowserContext"/> so cookies/storage
/// don't leak between tests.
/// </summary>
public sealed class AnonymousTierTests : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public AnonymousTierTests(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Login_page_renders_all_critical_elements()
    {
        await using var context = await _fixture.NewContextAsync();
        var page = await context.NewPageAsync();
        var login = new LoginPage(page);
        await page.GotoAsync("/Account/Login");

        // The five things a user needs to either sign in or self-recover —
        // any of these going missing is a P0 outage.
        await login.EmailInput.IsVisibleAsync().ContinueWith(t => t.Result.Should().BeTrue());
        await login.PasswordInput.IsVisibleAsync().ContinueWith(t => t.Result.Should().BeTrue());
        await login.SubmitButton.IsVisibleAsync().ContinueWith(t => t.Result.Should().BeTrue());
        await login.TenantPortalLink.IsVisibleAsync().ContinueWith(t => t.Result.Should().BeTrue());
        await login.ForgotPasswordLink.IsVisibleAsync().ContinueWith(t => t.Result.Should().BeTrue());

        // Brand identity — if these disappear we've broken the cobranding.
        (await page.TitleAsync()).Should().Contain("ALRTAM Company");
    }

    [Fact]
    public async Task Demo_credentials_button_populates_form_fields()
    {
        await using var context = await _fixture.NewContextAsync();
        var page = await context.NewPageAsync();
        var login = new LoginPage(page);
        await page.GotoAsync("/Account/Login");

        await login.DemoCredentialsButton.ClickAsync();

        // The dev-helper button must keep working — losing it would slow
        // every new developer's first login by 5 minutes.
        (await login.EmailInput.InputValueAsync()).Should().Be("admin@test.com");
        (await login.PasswordInput.InputValueAsync()).Should().Be("Admin@123");
    }

    [Fact]
    public async Task Show_password_toggle_flips_input_type()
    {
        await using var context = await _fixture.NewContextAsync();
        var page = await context.NewPageAsync();
        var login = new LoginPage(page);
        await page.GotoAsync("/Account/Login");
        await login.PasswordInput.FillAsync("secret123");

        (await login.PasswordInput.GetAttributeAsync("type")).Should().Be("password");

        await login.ShowPasswordToggle.ClickAsync();
        (await login.PasswordInput.GetAttributeAsync("type")).Should().Be("text");

        await login.ShowPasswordToggle.ClickAsync();
        (await login.PasswordInput.GetAttributeAsync("type")).Should().Be("password");
    }

    [Fact]
    public async Task Tenant_portal_link_navigates_to_tenant_login()
    {
        await using var context = await _fixture.NewContextAsync();
        var page = await context.NewPageAsync();
        var login = new LoginPage(page);
        await page.GotoAsync("/Account/Login");

        await login.TenantPortalLink.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        page.Url.Should().Contain("/TenantPortal/Login");
    }

    [Theory]
    [InlineData("/Reports")]
    [InlineData("/Reports/MonthEndClose")]
    [InlineData("/Dashboard")]
    public async Task Anonymous_request_to_protected_page_redirects_to_login(string path)
    {
        await using var context = await _fixture.NewContextAsync();
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync(path);

        // Playwright follows redirects, so we'll land on /Account/Login
        // with the original path baked into ReturnUrl.
        response!.Status.Should().Be(200);
        page.Url.Should().Contain("/Account/Login");
        page.Url.Should().Contain("ReturnUrl");
    }

    [Fact]
    public async Task Anonymous_request_to_Hangfire_dashboard_returns_401()
    {
        // Hangfire has its own DashboardAuthorizationFilter that returns 401
        // directly rather than redirecting to the MVC cookie-login flow. We
        // assert the gate is closed; the redirect is the MVC tier above's job.
        await using var context = await _fixture.NewContextAsync();
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync("/hangfire");

        response!.Status.Should().Be(401);
    }

    [Fact]
    public async Task Health_ready_returns_healthy_json()
    {
        await using var context = await _fixture.NewContextAsync();
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync("/health/ready");

        response!.Status.Should().Be(200);
        var body = await response.TextAsync();
        body.Should().Contain("\"status\":\"Healthy\"");
        body.Should().Contain("\"name\":\"database\"");
        body.Should().Contain("\"name\":\"hangfire\"");
    }

    [Fact]
    public async Task Wrong_password_keeps_user_on_login_with_error_visible()
    {
        await using var context = await _fixture.NewContextAsync();
        var page = await context.NewPageAsync();
        var login = new LoginPage(page);
        await page.GotoAsync("/Account/Login");

        await login.SubmitAsync("admin@test.com", "WrongPassword123!");

        page.Url.Should().Contain("/Account/Login");
        var body = await page.ContentAsync();
        body.Should().Contain("Invalid login attempt");
    }
}
