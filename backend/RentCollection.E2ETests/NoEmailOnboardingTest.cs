using System.Text.Json;
using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;
using RentCollection.E2ETests.Pages;

namespace RentCollection.E2ETests;

/// <summary>
/// The no-email onboarding flow (most tenants have no email):
///   admin creates a tenant WITHOUT an email → the system generates a temp
///   password and shows it on-screen → tenant logs in with Civil ID + temp
///   password (forced to change) → changes it in-app → re-logs in with the new
///   password. Also verifies GovernmentId defaults to the Civil ID so login works.
/// </summary>
[Collection("WorkflowSerial")]
public sealed class NoEmailOnboardingTest : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public NoEmailOnboardingTest(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Create_tenant_without_email_login_with_temp_password_then_change_it()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;

        var civilId = Math.Abs(Guid.NewGuid().GetHashCode()).ToString().PadLeft(12, '0').Substring(0, 12);
        var lastName = $"NoEmail{AdminSession.UniqueSuffix()}";
        int tenantId = 0;
        try
        {
            // 1. Create the tenant with NO email.
            var t = new TenantCreatePage(page);
            await t.GoToAsync();
            await t.FirstName.FillAsync("NoMail");
            await t.LastName.FillAsync(lastName);
            await t.Phone.FillAsync("99000123");
            await t.Paci.FillAsync(civilId);              // Client Civil ID (PACI)
            await t.BuildingPaci.FillAsync("NOEMAIL-NOMATCH");
            // deliberately DO NOT fill Email
            await t.SubmitAsync();

            // 2. Read the temp password shown on the landing page (shown once).
            await page.Locator("#newTenantCredential").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 8000 });
            var shownCivil = (await page.Locator("#ntCivilId").InnerTextAsync()).Trim();
            var tempPwd = (await page.Locator("#ntPwd").InnerTextAsync()).Trim();
            shownCivil.Should().Be(civilId, "GovernmentId must default to the entered Civil ID so login works");
            tempPwd.Should().NotBeNullOrWhiteSpace("a temp password must be generated and shown");

            // 3. Log in via the mobile API with the temp password.
            var login1 = await ctx.APIRequest.PostAsync("/api/tenant/login", new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string> { ["content-type"] = "application/json" },
                Data = JsonSerializer.Serialize(new { civilId, password = tempPwd }),
            });
            login1.Status.Should().Be(200, "temp-password login must succeed");
            var body1 = JsonSerializer.Deserialize<JsonElement>(await login1.TextAsync());
            body1.GetProperty("success").GetBoolean().Should().BeTrue();
            body1.GetProperty("mustChangePassword").GetBoolean().Should().BeTrue("first login must force a password change");
            var token = body1.GetProperty("data").GetProperty("token").GetString()!;
            tenantId = body1.GetProperty("data").GetProperty("tenantId").GetInt32();

            // 4. Change the password from the app.
            const string newPwd = "Tenant@New1";
            var change = await ctx.APIRequest.PostAsync("/api/tenant/change-password", new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["content-type"] = "application/json",
                    ["Authorization"] = "Bearer " + token,
                },
                Data = JsonSerializer.Serialize(new { tenantId, oldPassword = tempPwd, newPassword = newPwd }),
            });
            change.Status.Should().Be(200, "change-password must succeed with the correct current password");

            // 5. Re-login with the NEW password — and the force-change flag is cleared.
            var login2 = await ctx.APIRequest.PostAsync("/api/tenant/login", new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string> { ["content-type"] = "application/json" },
                Data = JsonSerializer.Serialize(new { civilId, password = newPwd }),
            });
            login2.Status.Should().Be(200);
            var body2 = JsonSerializer.Deserialize<JsonElement>(await login2.TextAsync());
            body2.GetProperty("success").GetBoolean().Should().BeTrue("new password must work");
            body2.GetProperty("mustChangePassword").GetBoolean().Should().BeFalse("force-change must be cleared after change");

            Console.WriteLine($"NOEMAIL_OK civilId={civilId} tenant={tenantId} temp→login→change→relogin");
        }
        finally
        {
            if (tenantId == 0)
                tenantId = await WorkflowSupport.Scalar<int>(
                    "SELECT ISNULL((SELECT TOP 1 TenantId FROM Tenants WHERE LastName=@l ORDER BY TenantId DESC),0)", ("@l", lastName));
            if (tenantId > 0)
                await WorkflowSupport.Exec(
                    $"DELETE FROM Notifications WHERE TenantId={tenantId}; DELETE FROM Tenants WHERE TenantId={tenantId};");
        }
    }

    [Fact]
    public async Task Admin_sets_the_password_tenant_logs_in_and_can_change_it()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;

        var civilId = Math.Abs(Guid.NewGuid().GetHashCode()).ToString().PadLeft(12, '0').Substring(0, 12);
        var lastName = $"AdminPwd{AdminSession.UniqueSuffix()}";
        const string adminPwd = "AdminSet@123";
        int tenantId = 0;
        try
        {
            // Create the tenant with NO email and an ADMIN-CHOSEN password.
            var t = new TenantCreatePage(page);
            await t.GoToAsync();
            await t.FirstName.FillAsync("Admin");
            await t.LastName.FillAsync(lastName);
            await t.Phone.FillAsync("99000456");
            await t.Paci.FillAsync(civilId);
            await t.BuildingPaci.FillAsync("ADMINPWD-NOMATCH");
            await page.Locator("input[name='InitialPassword']").FillAsync(adminPwd);
            await t.SubmitAsync();

            // The credential box echoes the admin's chosen password.
            await page.Locator("#newTenantCredential").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 8000 });
            (await page.Locator("#ntPwd").InnerTextAsync()).Trim().Should().Be(adminPwd);

            // Login with the admin-set password — NO forced change (admin chose it).
            var login = await ctx.APIRequest.PostAsync("/api/tenant/login", new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string> { ["content-type"] = "application/json" },
                Data = JsonSerializer.Serialize(new { civilId, password = adminPwd }),
            });
            login.Status.Should().Be(200);
            var body = JsonSerializer.Deserialize<JsonElement>(await login.TextAsync());
            body.GetProperty("success").GetBoolean().Should().BeTrue();
            body.GetProperty("mustChangePassword").GetBoolean().Should().BeFalse("admin-set password must not force a change");
            var token = body.GetProperty("data").GetProperty("token").GetString()!;
            tenantId = body.GetProperty("data").GetProperty("tenantId").GetInt32();

            // Tenant can still change it from the app.
            const string newPwd = "TenantOwn@1";
            var change = await ctx.APIRequest.PostAsync("/api/tenant/change-password", new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string> { ["content-type"] = "application/json", ["Authorization"] = "Bearer " + token },
                Data = JsonSerializer.Serialize(new { tenantId, oldPassword = adminPwd, newPassword = newPwd }),
            });
            change.Status.Should().Be(200);

            var relogin = await ctx.APIRequest.PostAsync("/api/tenant/login", new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string> { ["content-type"] = "application/json" },
                Data = JsonSerializer.Serialize(new { civilId, password = newPwd }),
            });
            relogin.Status.Should().Be(200);
            JsonSerializer.Deserialize<JsonElement>(await relogin.TextAsync())
                .GetProperty("success").GetBoolean().Should().BeTrue("tenant's own new password must work");

            Console.WriteLine($"ADMINPWD_OK civilId={civilId} tenant={tenantId} adminSet→login(noForce)→change→relogin");
        }
        finally
        {
            if (tenantId == 0)
                tenantId = await WorkflowSupport.Scalar<int>(
                    "SELECT ISNULL((SELECT TOP 1 TenantId FROM Tenants WHERE LastName=@l ORDER BY TenantId DESC),0)", ("@l", lastName));
            if (tenantId > 0)
                await WorkflowSupport.Exec(
                    $"DELETE FROM Notifications WHERE TenantId={tenantId}; DELETE FROM Tenants WHERE TenantId={tenantId};");
        }
    }
}
