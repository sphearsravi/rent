using System.Text.Json;
using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;
using RentCollection.E2ETests.Pages;

namespace RentCollection.E2ETests;

/// <summary>
/// Step-by-step: an admin creates a tenant with a Civil ID ("username") + a
/// password (no email), then that tenant logs in via the mobile API.
///
/// Point it at the LIVE SmarterASP backend to run it against production:
///   RC_E2E_BASE_URL=http://<DEMO_HOST>
///   RC_E2E_DB="Data Source=<DB_HOST>;Initial Catalog=<DB_NAME>;User Id=<DB_USER>;Password=<DB_PASSWORD>;Encrypt=True;TrustServerCertificate=True;"
///   RC_E2E_HEADED=1 RC_E2E_SLOWMO=1200
/// Creates one tenant and deletes it again (self-cleaning).
/// </summary>
[Collection("WorkflowSerial")]
public sealed class TenantLoginLiveTest : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public TenantLoginLiveTest(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Create_tenant_with_username_password_then_login()
    {
        // The credentials this test will create and log in with.
        var username = Math.Abs(Guid.NewGuid().GetHashCode()).ToString().PadLeft(12, '0').Substring(0, 12); // Civil ID
        const string password = "Tenant@Pass1";
        var lastName = $"Live{AdminSession.UniqueSuffix()}";
        int tenantId = 0;

        try
        {
            // ── STEP 1 — Admin signs into the web app ─────────────────────
            Console.WriteLine($"STEP 1: admin sign-in @ {_fixture.BaseUrl}");
            var (ctx, page) = await AdminSession.SignInAsync(_fixture);
            await using var _ = ctx;

            // ── STEP 2 — Open Tenants → Create and fill the form ──────────
            Console.WriteLine($"STEP 2: fill new tenant (Civil ID={username}, password set, NO email)");
            var t = new TenantCreatePage(page);
            await t.GoToAsync();
            await t.FirstName.FillAsync("Live");
            await t.LastName.FillAsync(lastName);
            await t.Phone.FillAsync("99001111");
            await t.Paci.FillAsync(username);                       // Civil ID = the login "username"
            await t.BuildingPaci.FillAsync("LIVE-NOMATCH");
            await page.Locator("input[name='InitialPassword']").FillAsync(password);  // admin-set password

            // ── STEP 3 — Save; read back the login shown on-screen ────────
            Console.WriteLine("STEP 3: save tenant → read the login box");
            await t.SubmitAsync();
            await page.Locator("#newTenantCredential").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            var shownCivil = (await page.Locator("#ntCivilId").InnerTextAsync()).Trim();
            var shownPwd = (await page.Locator("#ntPwd").InnerTextAsync()).Trim();
            shownCivil.Should().Be(username, "the login Civil ID must be the one entered");
            shownPwd.Should().Be(password, "the box must show the admin-set password");

            // ── STEP 4 — Tenant logs in via the mobile API (LIVE) ─────────
            Console.WriteLine("STEP 4: tenant login via /api/tenant/login");
            var login = await ctx.APIRequest.PostAsync("/api/tenant/login", new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string> { ["content-type"] = "application/json" },
                Data = JsonSerializer.Serialize(new { civilId = username, password }),
            });
            login.Status.Should().Be(200, "the new tenant must be able to log in");
            var body = JsonSerializer.Deserialize<JsonElement>(await login.TextAsync());
            body.GetProperty("success").GetBoolean().Should().BeTrue("login must succeed");
            var token = body.GetProperty("data").GetProperty("token").GetString();
            token.Should().NotBeNullOrEmpty("login must return a JWT");
            tenantId = body.GetProperty("data").GetProperty("tenantId").GetInt32();

            Console.WriteLine($"LOGIN_LIVE_OK  civilId={username}  password=***  tenantId={tenantId}  token=***");
        }
        finally
        {
            // ── Cleanup — remove the tenant we created ────────────────────
            if (tenantId == 0)
                tenantId = await WorkflowSupport.Scalar<int>(
                    "SELECT ISNULL((SELECT TOP 1 TenantId FROM Tenants WHERE LastName=@l ORDER BY TenantId DESC),0)", ("@l", lastName));
            if (tenantId > 0)
                await WorkflowSupport.Exec(
                    $"DELETE FROM Notifications WHERE TenantId={tenantId}; DELETE FROM Tenants WHERE TenantId={tenantId};");
        }
    }
}
