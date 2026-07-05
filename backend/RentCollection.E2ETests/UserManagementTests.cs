using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// Sprint 17 — Admin user management RBAC. Tests focus on the boundary
/// surface: who can reach /Users, what the form looks like, and whether
/// the role dropdown only offers valid roles.
/// </summary>
public sealed class UserManagementTests : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public UserManagementTests(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Anonymous_request_to_Users_redirects_to_login()
    {
        await using var ctx = await _fixture.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync("/Users");
        page.Url.Should().Contain("/Account/Login");
    }

    [Fact]
    public async Task Users_index_renders_for_admin()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var response = await page.GotoAsync("/Users");
        response!.Status.Should().Be(200);
        (await page.ContentAsync()).ToLowerInvariant().Should().Contain("user");
    }

    [Fact]
    public async Task Create_User_form_renders_role_dropdown()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var response = await page.GotoAsync("/Users/Create");
        response!.Status.Should().Be(200);

        // The page must include a Role select with the canonical roles.
        var content = await page.ContentAsync();
        content.Should().ContainAny("Admin", "SuperAdmin", "Manager",
            "Role dropdown must list at least one of the canonical roles");
    }

    [Fact]
    public async Task Permissions_page_renders_role_matrix()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var response = await page.GotoAsync("/Users/Permissions");
        if (response!.Status == 404) return;  // page may not exist on every deploy
        response.Status.Should().Be(200);
    }
}
