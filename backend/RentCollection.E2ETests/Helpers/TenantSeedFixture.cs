using Microsoft.EntityFrameworkCore;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;

namespace RentCollection.E2ETests.Helpers;

/// <summary>
/// Ensures a known-credentials tenant exists in the live database so the
/// authenticated journey tests have a stable login. Idempotent — runs once
/// per test class via <see cref="IClassFixture{TFixture}"/>; subsequent
/// runs upsert rather than duplicate.
///
/// Tenant footprint:
///   • Email/Civil ID/PASSWORD are constants below so a developer can also
///     log in by hand for ad-hoc debugging.
///   • A test property/unit/lease pair backs the tenant so the dashboard
///     has data to render.
///   • At least one Invoice exists so the PDF-download test has a target.
///
/// Connection string comes from <c>RC_E2E_DB</c> env var (falls back to
/// the dev default), so CI points at the dockerised SQL Server in the
/// `e2e` job while local devs hit the dev DB.
/// </summary>
public sealed class TenantSeedFixture : IAsyncLifetime
{
    public const string CivilId = "999000000001";
    public const string Password = "E2eTest@123";
    public const string Email = "e2e-test-tenant@example.com";
    public const int CompanyId = 1;

    /// <summary>Resolved tenant id — populated by <see cref="InitializeAsync"/>.</summary>
    public int TenantId { get; private set; }
    public int InvoiceId { get; private set; }

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("RC_E2E_DB")
        ?? @"Server=RAVIWST\SQLEXPRESS;Database=RentCollectionDB;Trusted_Connection=true;TrustServerCertificate=true;";

    public async Task InitializeAsync()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        await using var ctx = new ApplicationDbContext(opts);

        // 1. Tenant — upsert by GovernmentId. Status MUST be Active or the
        //    login flow rejects it before checking the password.
        var tenant = await ctx.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.GovernmentId == CivilId);
        if (tenant == null)
        {
            tenant = new Tenant
            {
                CompanyId = CompanyId,
                FirstName = "E2E",
                LastName = "Test",
                Email = Email,
                Phone = "99000000",
                GovernmentId = CivilId,
                Status = "Active",
                MustChangePasswordOnNextLogin = false,
            };
            ctx.Tenants.Add(tenant);
        }
        // Always reset the password — guards against a previous test changing
        // it. BCrypt matches what TenantPortalController.Login expects.
        tenant.Password = BCrypt.Net.BCrypt.HashPassword(Password);
        tenant.Status = "Active";
        tenant.MustChangePasswordOnNextLogin = false;
        await ctx.SaveChangesAsync();
        TenantId = tenant.TenantId;

        // 2. Test property + unit if not already present. Use a recognisable
        //    name prefix so a human can identify the test fixture in the UI.
        var property = await ctx.Properties
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.PropertyName == "E2E Test Tower");
        if (property == null)
        {
            property = new Property
            {
                CompanyId = CompanyId,
                PropertyName = "E2E Test Tower",
                PropertyType = "Residential",
                Address = "E2E test address",
                Status = "Active",
                PACINumber = "PACI-E2E-001",
            };
            ctx.Properties.Add(property);
            await ctx.SaveChangesAsync();
        }

        var unit = await ctx.Units
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.PropertyId == property.PropertyId && u.UnitNumber == "E2E-1");
        if (unit == null)
        {
            unit = new Unit
            {
                PropertyId = property.PropertyId,
                UnitNumber = "E2E-1",
                UnitType = "Apartment",
                MonthlyRent = 500m,
                Status = "Occupied",
            };
            ctx.Units.Add(unit);
            await ctx.SaveChangesAsync();
        }

        // 3. Active lease
        var lease = await ctx.Leases
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.TenantId == TenantId && l.UnitId == unit.UnitId);
        if (lease == null)
        {
            lease = new Lease
            {
                TenantId = TenantId,
                UnitId = unit.UnitId,
                LeaseStartDate = DateTime.UtcNow.AddMonths(-3),
                LeaseEndDate = DateTime.UtcNow.AddMonths(9),
                MonthlyRent = 500m,
                SecurityDeposit = 500m,
                Status = "Active",
            };
            ctx.Leases.Add(lease);
            await ctx.SaveChangesAsync();
        }

        // 4. At least one Invoice for the PDF-download test.
        var invoice = await ctx.Invoices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.LeaseId == lease.LeaseId && i.InvoiceType == "Rent");
        if (invoice == null)
        {
            invoice = new Invoice
            {
                LeaseId = lease.LeaseId,
                TenantId = TenantId,
                InvoiceDate = DateTime.UtcNow.AddDays(-7),
                DueDate = DateTime.UtcNow.AddDays(7),
                TotalAmount = 500m,
                PaidAmount = 0m,
                Status = "Pending",
                InvoiceType = "Rent",
                Notes = "E2E test invoice",
            };
            ctx.Invoices.Add(invoice);
            await ctx.SaveChangesAsync();
        }
        InvoiceId = invoice.InvoiceId;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
