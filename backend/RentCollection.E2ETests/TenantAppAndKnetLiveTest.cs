using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// Tenant-app side, live: signs into the TENANT PORTAL (same login the mobile app
/// uses — Civil ID + password) for the most-recent workflow tenant, views the
/// dashboard + invoices, then pays a fresh invoice via the KNET card path using
/// the app's KNET test simulator (the demo has no KNET merchant credentials, so
/// the simulator stands in for entering a KNET test card on the gateway).
/// Data is KEPT (no teardown). Watch it:
///   RC_E2E_HEADED=1 RC_E2E_SLOWMO=700 dotnet test --filter TenantAppAndKnetLiveTest
/// </summary>
[Collection("WorkflowSerial")]
public sealed class TenantAppAndKnetLiveTest : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public TenantAppAndKnetLiveTest(E2EFixture fixture) => _fixture = fixture;

    private const string TenantPassword = "Tenant@12345";   // set by FullWorkflowManualTest

    [Fact]
    public async Task Tenant_portal_and_knet_test_card()
    {
        var baseUrl = Environment.GetEnvironmentVariable("RC_E2E_BASE_URL") ?? "http://localhost:5032";

        // Use the most-recent ManualWF tenant (created by FullWorkflowManualTest).
        var tenantId = await WorkflowSupport.Scalar<int>(
            "SELECT ISNULL((SELECT TOP 1 TenantId FROM Tenants WHERE LastName LIKE 'ManualWF%' ORDER BY TenantId DESC),0)");
        tenantId.Should().BeGreaterThan(0, "run FullWorkflowManualTest first — this reuses its tenant");
        var civilId = await WorkflowSupport.Scalar<string>("SELECT GovernmentId FROM Tenants WHERE TenantId=@t", ("@t", tenantId));
        var leaseId = await WorkflowSupport.Scalar<int>("SELECT TOP 1 LeaseId FROM Leases WHERE TenantId=@t ORDER BY LeaseId DESC", ("@t", tenantId));
        var propertyId = await WorkflowSupport.Scalar<int>(
            "SELECT TOP 1 u.PropertyId FROM Units u JOIN Leases l ON l.UnitId=u.UnitId WHERE l.LeaseId=@l", ("@l", leaseId));

        await using var ctx = await _fixture.NewContextAsync();
        var page = await ctx.NewPageAsync();

        // ── 1. TENANT PORTAL LOGIN (Civil ID + password) ─────────────────
        await page.GotoAsync("/TenantPortal/Login", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("#civilId").FillAsync(civilId!);
        await page.Locator("#password").FillAsync(TenantPassword);
        await page.EvaluateAsync("() => document.getElementById('civilId').closest('form').submit()");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        page.Url.Should().NotContain("/TenantPortal/Login", "tenant must sign into the portal with Civil ID + password");

        // ── 2. VIEW DASHBOARD + INVOICES ─────────────────────────────────
        await page.GotoAsync("/TenantPortal/Dashboard", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.GotoAsync("/TenantPortal/MakePayment", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var invHtml = await page.ContentAsync();
        invHtml.ToLowerInvariant().Should().ContainAny("invoice", "pay", "due", "kwd");

        // ── 3. KNET PAYMENT (test simulator = "test card") ───────────────
        // Fresh unpaid invoice to pay by KNET (distinct amount so it's easy to spot).
        const decimal knetAmount = 200.000m;
        // Date it NEXT month so it doesn't collide with the current rent invoice
        // on the unique (LeaseId, InvoiceType, InvoiceDate) index.
        await WorkflowSupport.Exec($@"
            INSERT INTO Invoices (LeaseId, TenantId, InvoiceDate, DueDate, TotalAmount, PaidAmount, Status, InvoiceType, Notes, CreatedDate)
            VALUES ({leaseId}, {tenantId}, DATEADD(MONTH,1,GETUTCDATE()), EOMONTH(DATEADD(MONTH,1,GETUTCDATE())), 200.000, 0, 'Pending', 'Rent', 'KNET test-card invoice', GETUTCDATE());");
        var knetInvoiceId = await WorkflowSupport.Scalar<int>(
            "SELECT TOP 1 InvoiceId FROM Invoices WHERE TenantId=@t AND Notes='KNET test-card invoice' ORDER BY InvoiceId DESC", ("@t", tenantId));
        knetInvoiceId.Should().BeGreaterThan(0);

        // Initiate KNET (AllowAnonymous) — creates a Pending KNET payment (PaymentType=1).
        var initiate = await ctx.APIRequest.PostAsync($"{baseUrl}/api/payments/knet/initiate", new()
        {
            Headers = new Dictionary<string, string> { ["content-type"] = "application/json" },
            Data = JsonSerializer.Serialize(new { invoiceId = knetInvoiceId, tenantId, propertyId, leaseId, amount = knetAmount }),
        });
        initiate.Status.Should().BeInRange(200, 299, "KNET initiate must succeed: " + await initiate.TextAsync());
        var knetPaymentId = await WorkflowSupport.Scalar<int>(
            "SELECT ISNULL((SELECT TOP 1 PaymentId FROM Payments WHERE TenantId=@t AND PaymentType=1 ORDER BY PaymentId DESC),0)", ("@t", tenantId));
        knetPaymentId.Should().BeGreaterThan(0, "initiate must create a KNET payment row");

        // Complete the card payment via the test simulator (success = approved card).
        var complete = await ctx.APIRequest.PostAsync($"{baseUrl}/api/payments/knet/test/complete", new()
        {
            Headers = new Dictionary<string, string> { ["content-type"] = "application/json" },
            Data = JsonSerializer.Serialize(new { paymentId = knetPaymentId, success = true }),
        });
        complete.Status.Should().BeInRange(200, 299, "KNET test-complete must succeed: " + await complete.TextAsync());

        // KNET auto-approves — payment Completed(2), invoice Paid, no approval step.
        (await WorkflowSupport.Scalar<int>("SELECT Status FROM Payments WHERE PaymentId=@p", ("@p", knetPaymentId)))
            .Should().Be(2, "KNET success → Payment Completed(2)");
        (await WorkflowSupport.Scalar<string>("SELECT Status FROM Invoices WHERE InvoiceId=@i", ("@i", knetInvoiceId)))
            .Should().Be("Paid", "a full KNET payment flips the invoice to Paid");

        // Receipt available.
        var receipt = await ctx.APIRequest.GetAsync($"{baseUrl}/api/payments/{knetPaymentId}/receipt.pdf");
        receipt.Status.Should().Be(200);

        // ── 4. TENANT SEES THE KNET INVOICE PAID (mobile API) ────────────
        var login = await ctx.APIRequest.PostAsync($"{baseUrl}/api/tenant/login", new()
        { DataObject = new { CivilId = civilId, Password = TenantPassword } });
        login.Status.Should().Be(200);
        var token = (await login.JsonAsync()).Value.GetProperty("data").GetProperty("token").GetString();
        var invResp = await ctx.APIRequest.GetAsync($"{baseUrl}/api/tenant/{tenantId}/invoices",
            new() { Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" } });
        (await invResp.TextAsync()).Should().Contain("Paid");

        var sb = new StringBuilder();
        sb.AppendLine("==================== TENANT APP + KNET (test card) — LIVE, kept ====================");
        sb.AppendLine($"  Tenant portal login OK →  Civil ID: {civilId}   Password: {TenantPassword}   (tenant {tenantId})");
        sb.AppendLine($"  KNET invoice   : id={knetInvoiceId}  amount={knetAmount:0.000} KWD  → status=Paid");
        sb.AppendLine($"  KNET payment   : id={knetPaymentId}  status=Completed(2)  (auto-approved, receipt=200)");
        sb.AppendLine("  Note: KNET used the app's TEST SIMULATOR (no merchant creds on demo) — stands in for a KNET test card.");
        sb.AppendLine("====================================================================================");
        Console.WriteLine(sb.ToString());
        Console.WriteLine($"TENANTAPP_KNET_OK tenant={tenantId} civilId={civilId} knetInvoice={knetInvoiceId} knetPayment={knetPaymentId} paid");
    }
}
