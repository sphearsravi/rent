using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;
using RentCollection.E2ETests.Pages;
using RentCollection.Infrastructure.Data;

namespace RentCollection.E2ETests;

/// <summary>
/// Reproduces the user-reported failure: submitting the tenant form produced
///   "The value '' is invalid." + "Your account isn't linked to a company."
/// Cause: the empty hidden CompanyId field + an admin with no company claim.
/// This test drives the REAL flow (never sets CompanyId manually) and asserts
/// the tenant is created with a resolved company and NO error banner.
/// </summary>
public sealed class TenantCreateCompanyResolveTest : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public TenantCreateCompanyResolveTest(E2EFixture fixture) => _fixture = fixture;

    private static string ConnString =>
        Environment.GetEnvironmentVariable("RC_E2E_DB")
        ?? @"Server=RAVIWST\SQLEXPRESS;Database=RentCollectionDB;Trusted_Connection=true;TrustServerCertificate=true;";

    [Fact]
    public async Task Submit_without_manual_company_resolves_and_creates()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var t = new TenantCreatePage(page);
        await t.GoToAsync();

        var suffix = AdminSession.UniqueSuffix();
        var firstName = "CoResolve";
        var lastName = $"T{suffix}";
        var paci = Math.Abs(Guid.NewGuid().GetHashCode()).ToString().PadLeft(12, '0').Substring(0, 12);

        // Fill ONLY the visible essentials — deliberately DO NOT set CompanyId,
        // and use a non-matching Building PACI so no lease side-effects occur.
        await t.FirstName.FillAsync(firstName);
        await t.LastName.FillAsync(lastName);
        await t.Email.FillAsync($"co+{suffix}@example.com");
        await t.Phone.FillAsync("99000321");
        await t.Paci.FillAsync(paci);
        await t.BuildingPaci.FillAsync("NO-MATCH-BUILDING");

        var landed = await t.SubmitAsync();

        // The error banner must NOT appear.
        var bannerCount = await page.Locator("#formErrorBanner").CountAsync();

        var opts = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnString).Options;
        int dbId = 0, dbCompany = 0;
        await using (var db = new ApplicationDbContext(opts))
        {
            var row = await db.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(x => x.LastName == lastName && x.FirstName == firstName);
            if (row != null) { dbId = row.TenantId; dbCompany = row.CompanyId; }
        }

        Console.WriteLine($"CO_RESOLVE landed=[{landed}] bannerCount=[{bannerCount}] dbId=[{dbId}] dbCompany=[{dbCompany}]");

        try
        {
            landed.Should().NotContain("/Tenants/Create", "the create must now succeed and redirect");
            bannerCount.Should().Be(0, "no error banner should appear on a valid submit");
            dbId.Should().BeGreaterThan(0, "the tenant must be persisted");
            dbCompany.Should().Be(1, "CompanyId must resolve from the admin's DB record");
        }
        finally
        {
            if (dbId > 0)
            {
                await using var db = new ApplicationDbContext(opts);
                await db.Tenants.Where(x => x.TenantId == dbId).ExecuteDeleteAsync();
            }
        }
    }
}
