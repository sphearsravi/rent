using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;
using RentCollection.E2ETests.Pages;
using RentCollection.Infrastructure.Data;

namespace RentCollection.E2ETests;

/// <summary>
/// DEMO + FIX VERIFICATION (watch headed + slow-mo): create a tenant for
/// property "test1" and prove the operator's EXPLICIT unit pick is honored
/// over the Building-PACI auto-assign.
///   RC_E2E_HEADED=1 RC_E2E_SLOWMO=1500 dotnet test --filter TenantForTest1Demo
/// The Building PACI matches test1 (auto-assign would grab unit 719), but we
/// explicitly pick unit 718 — the lease MUST land on 718. Self-cleans.
/// </summary>
public sealed class TenantForTest1Demo : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public TenantForTest1Demo(E2EFixture fixture) => _fixture = fixture;

    private const string Test1PropertyId = "55";
    private const string Test1BuildingPaci = "234123123123";
    private const int ExplicitUnitId = 718;   // NOT the auto-assign pick (719)
    private const int AutoAssignWouldPick = 719;

    private static string ConnString =>
        Environment.GetEnvironmentVariable("RC_E2E_DB")
        ?? @"Server=RAVIWST\SQLEXPRESS;Database=RentCollectionDB;Trusted_Connection=true;TrustServerCertificate=true;";

    [Fact]
    public async Task Explicit_unit_pick_is_honored_over_PACI_autoassign()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var t = new TenantCreatePage(page);
        await t.GoToAsync();

        var suffix = AdminSession.UniqueSuffix();
        var firstName = "Ravi";
        var lastName = $"ForTest1_{suffix}";
        var paci = Math.Abs(Guid.NewGuid().GetHashCode()).ToString().PadLeft(12, '0').Substring(0, 12);

        await t.FillEssentialsAsync(firstName, lastName, $"ravi+{suffix}@example.com",
            "99001234", paci, Test1BuildingPaci);

        await t.ExpandMoreDetailsAsync();
        await t.SelectPropertyAsync(Test1PropertyId);
        await t.SelectUnitAsync(ExplicitUnitId.ToString());

        (await t.CheckHtml5ValidityAsync()).Should().Be("VALID");
        var landed = await t.SubmitAsync();
        landed.Should().NotContain("/Tenants/Create");

        var opts = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnString).Options;
        int tenantId = 0, leaseId = 0, leaseUnitId = 0;
        await using (var db = new ApplicationDbContext(opts))
        {
            var tenant = await db.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(x => x.LastName == lastName && x.FirstName == firstName);
            if (tenant != null)
            {
                tenantId = tenant.TenantId;
                var lease = await db.Leases.AsNoTracking().FirstOrDefaultAsync(l => l.TenantId == tenantId);
                if (lease != null) { leaseId = lease.LeaseId; leaseUnitId = lease.UnitId; }
            }
        }

        Console.WriteLine($"FIX_VERIFY tenantId={tenantId} leaseId={leaseId} leaseUnitId={leaseUnitId} " +
            $"(explicit={ExplicitUnitId}, autoWouldPick={AutoAssignWouldPick})");

        try
        {
            tenantId.Should().BeGreaterThan(0, "tenant must be created");
            leaseId.Should().BeGreaterThan(0, "a lease must be created");
            leaseUnitId.Should().Be(ExplicitUnitId,
                $"the lease must use the explicitly-picked unit {ExplicitUnitId}, not the auto-assign pick {AutoAssignWouldPick}");
        }
        finally
        {
            await using var db = new ApplicationDbContext(opts);
            if (leaseId > 0) await db.Leases.Where(l => l.LeaseId == leaseId).ExecuteDeleteAsync();
            if (leaseUnitId > 0) await db.Units.Where(u => u.UnitId == leaseUnitId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, "Vacant"));
            if (tenantId > 0) await db.Tenants.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        }
    }
}
