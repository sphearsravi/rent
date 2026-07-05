using Microsoft.Playwright;
using RentCollection.E2ETests.Pages;

namespace RentCollection.E2ETests.Helpers;

/// <summary>
/// Shared admin sign-in for the workflow E2E suites (Sprint 15-18).
/// Each test creates a fresh <see cref="IBrowserContext"/> via the fixture
/// and uses <see cref="SignInAsync"/> to land on a post-login page with
/// the auth cookie set. Cleaner than duplicating the same 15 lines in 11
/// test classes.
///
/// Uses the seeded `admin@test.com / Admin@123`. If those creds change,
/// update <see cref="AdminEmail"/> + <see cref="AdminPassword"/> and the
/// whole suite picks it up.
/// </summary>
public static class AdminSession
{
    public const string AdminEmail = "admin@test.com";
    public const string AdminPassword = "Admin@123";

    public static async Task<(IBrowserContext ctx, IPage page)> SignInAsync(E2EFixture fixture)
    {
        var ctx = await fixture.NewContextAsync();
        var page = await ctx.NewPageAsync();
        var login = new LoginPage(page);
        await page.GotoAsync("/Account/Login");
        await login.SubmitAsync(AdminEmail, AdminPassword);
        if (page.Url.Contains("/Account/Login"))
            throw new InvalidOperationException(
                "Admin sign-in did not redirect away from /Account/Login. " +
                "Check the seeded user exists with the expected password.");
        return (ctx, page);
    }

    /// <summary>
    /// Short uppercase-hex suffix (6 chars) for generating unique IDs in
    /// tests. Prevents collisions when the same test re-runs against the
    /// same DB and the entity has a unique constraint.
    /// </summary>
    public static string UniqueSuffix() =>
        Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
}
