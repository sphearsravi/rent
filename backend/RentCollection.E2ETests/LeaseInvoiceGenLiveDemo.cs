using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;
using RentCollection.E2ETests.Pages;

namespace RentCollection.E2ETests;

/// <summary>
/// LIVE, watch-along demo of INVOICE GENERATION:
///   1. A Pending lease is Activated in the admin UI → the app issues the
///      Deposit invoice atomically (activation-triggered).
///   2. "Generate Monthly Invoices" is clicked → the recurring-job logic
///      creates the lease's Rent invoice for the current month.
/// Watch it: RC_E2E_HEADED=1. Requires RC_DEMO_LEASE + admin creds.
/// </summary>
[Collection("WorkflowSerial")]
public sealed class LeaseInvoiceGenLiveDemo : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public LeaseInvoiceGenLiveDemo(E2EFixture fixture) => _fixture = fixture;

    private static Task<int> InvCount(int leaseId, string type) => WorkflowSupport.Scalar<int>(
        "SELECT COUNT(*) FROM Invoices WHERE LeaseId=@l AND InvoiceType=@t", ("@l", leaseId), ("@t", type));

    [Fact]
    public async Task Activate_lease_then_generate_creates_deposit_and_rent_invoices()
    {
        var leaseId = int.Parse(Environment.GetEnvironmentVariable("RC_DEMO_LEASE") ?? "694");
        var adminEmail = Environment.GetEnvironmentVariable("RC_ADMIN_EMAIL") ?? throw new InvalidOperationException("Set RC_ADMIN_EMAIL");
        var adminPass = Environment.GetEnvironmentVariable("RC_ADMIN_PASS") ?? throw new InvalidOperationException("Set RC_ADMIN_PASS");

        // ── BEFORE ──
        var statusBefore = await WorkflowSupport.Scalar<string>("SELECT Status FROM Leases WHERE LeaseId=@l", ("@l", leaseId));
        Console.WriteLine($"GEN_BEFORE lease {leaseId} status=[{statusBefore}]  Deposit invoices={await InvCount(leaseId, "Deposit")}  Rent invoices={await InvCount(leaseId, "Rent")}");

        await using var ctx = await _fixture.NewContextAsync();
        var page = await ctx.NewPageAsync();
        page.Dialog += async (_, d) => await d.AcceptAsync();   // auto-accept the Activate confirm()

        // ── Admin login ──
        var login = new LoginPage(page);
        await page.GotoAsync("/Account/Login", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await login.SubmitAsync(adminEmail, adminPass);
        await page.WaitForTimeoutAsync(1500);
        page.Url.Should().NotContain("/Account/Login", "admin must sign in");

        // ── STEP 1: Activate the lease (app issues the Deposit invoice) ──
        Console.WriteLine($"GEN_STEP_1 activate lease {leaseId} (app issues Deposit invoice)");
        await page.GotoAsync($"/Leases/Details/{leaseId}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.GetByRole(AriaRole.Button, new() { Name = "Activate" }).First.ClickAsync(new() { NoWaitAfter = true });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForTimeoutAsync(1500);

        var statusAfterActivate = await WorkflowSupport.Scalar<string>("SELECT Status FROM Leases WHERE LeaseId=@l", ("@l", leaseId));
        Console.WriteLine($"GEN_STEP_1_RESULT lease status=[{statusAfterActivate}]  Deposit invoices={await InvCount(leaseId, "Deposit")} (activation created it)");

        // ── STEP 2: Generate Monthly Invoices (job issues the Rent invoice) ──
        Console.WriteLine("GEN_STEP_2 click 'Generate Monthly Invoices' (recurring-job logic → Rent invoice)");
        await page.GotoAsync("/Invoices", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.GetByRole(AriaRole.Button, new() { Name = "Generate Monthly Invoices" }).First.ClickAsync(new() { NoWaitAfter = true });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForTimeoutAsync(2000);

        var rentCount = await InvCount(leaseId, "Rent");
        Console.WriteLine($"GEN_STEP_2_RESULT lease {leaseId} Rent invoices={rentCount}");

        // ── Show the generated rows ──
        var deposit = await WorkflowSupport.Scalar<decimal>("SELECT ISNULL((SELECT TOP 1 TotalAmount FROM Invoices WHERE LeaseId=@l AND InvoiceType='Deposit'),0)", ("@l", leaseId));
        var rent = await WorkflowSupport.Scalar<decimal>("SELECT ISNULL((SELECT TOP 1 TotalAmount FROM Invoices WHERE LeaseId=@l AND InvoiceType='Rent'),0)", ("@l", leaseId));
        var rentDue = await WorkflowSupport.Scalar<string>("SELECT CONVERT(varchar,(SELECT TOP 1 DueDate FROM Invoices WHERE LeaseId=@l AND InvoiceType='Rent'),23)", ("@l", leaseId));
        Console.WriteLine($"GEN_DONE lease {leaseId}: Deposit invoice={deposit:0.000} KWD, Rent invoice={rent:0.000} KWD due {rentDue}");

        statusAfterActivate.Should().Be("Active", "activation must flip the lease Active");
        (await InvCount(leaseId, "Deposit")).Should().BeGreaterThanOrEqualTo(1, "activation issues the deposit invoice");
        rentCount.Should().BeGreaterThanOrEqualTo(1, "the generation job issues the rent invoice");
    }
}
