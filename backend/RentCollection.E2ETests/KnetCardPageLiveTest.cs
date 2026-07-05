using System.Text.Json;
using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// LIVE, self-contained: signs into the tenant portal as the latest ManualWF
/// tenant, initiates a KNET payment, opens the KNET payment CARD page
/// (/TenantPortal/TestPayment — the in-app card screen shown when
/// Knet:UseTestMode=true), and clicks Complete Payment, settling the invoice.
/// Watch: RC_E2E_HEADED=1 RC_E2E_SLOWMO=900 dotnet test --filter KnetCardPageLiveTest
/// </summary>
[Collection("WorkflowSerial")]
public sealed class KnetCardPageLiveTest : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public KnetCardPageLiveTest(E2EFixture fixture) => _fixture = fixture;

    private const string TenantPassword = "Tenant@12345";

    [Fact]
    public async Task Open_knet_card_page_and_complete()
    {
        var baseUrl = Environment.GetEnvironmentVariable("RC_E2E_BASE_URL") ?? "http://localhost:5032";

        var tenantId = await WorkflowSupport.Scalar<int>(
            "SELECT ISNULL((SELECT TOP 1 TenantId FROM Tenants WHERE LastName LIKE 'ManualWF%' ORDER BY TenantId DESC),0)");
        var civilId = await WorkflowSupport.Scalar<string>("SELECT GovernmentId FROM Tenants WHERE TenantId=@t", ("@t", tenantId));
        var leaseId = await WorkflowSupport.Scalar<int>("SELECT TOP 1 LeaseId FROM Leases WHERE TenantId=@t ORDER BY LeaseId DESC", ("@t", tenantId));
        var propertyId = await WorkflowSupport.Scalar<int>(
            "SELECT TOP 1 u.PropertyId FROM Units u JOIN Leases l ON l.UnitId=u.UnitId WHERE l.LeaseId=@l", ("@l", leaseId));

        await using var ctx = await _fixture.NewContextAsync();
        var page = await ctx.NewPageAsync();

        // 0. Fresh unpaid invoice (far-future date so it can't collide on the unique
        //    LeaseId+Type+Date index), then initiate KNET → get the card-page URL.
        await WorkflowSupport.Exec($@"
            INSERT INTO Invoices (LeaseId, TenantId, InvoiceDate, DueDate, TotalAmount, PaidAmount, Status, InvoiceType, Notes, CreatedDate)
            VALUES ({leaseId}, {tenantId}, DATEADD(YEAR,1,GETUTCDATE()), EOMONTH(DATEADD(YEAR,1,GETUTCDATE())), 175.000, 0, 'Pending', 'Rent', 'KNET live card demo', GETUTCDATE());");
        var invoiceId = await WorkflowSupport.Scalar<int>(
            "SELECT TOP 1 InvoiceId FROM Invoices WHERE TenantId=@t AND Notes='KNET live card demo' AND Status='Pending' ORDER BY InvoiceId DESC", ("@t", tenantId));

        var initiate = await ctx.APIRequest.PostAsync($"{baseUrl}/api/payments/knet/initiate", new()
        {
            Headers = new Dictionary<string, string> { ["content-type"] = "application/json" },
            Data = JsonSerializer.Serialize(new { invoiceId, tenantId, propertyId, leaseId, amount = 175.000m }),
        });
        var initJson = await initiate.JsonAsync();
        initJson.Value.GetProperty("success").GetBoolean().Should().BeTrue("KNET initiate: " + await initiate.TextAsync());
        var cardUrl = initJson.Value.GetProperty("redirectUrl").GetString()!;   // /TenantPortal/TestPayment?...

        // 1. Tenant portal login (Civil ID + password — same as the mobile app).
        await page.GotoAsync("/TenantPortal/Login", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("#civilId").FillAsync(civilId!);
        await page.Locator("#password").FillAsync(TenantPassword);
        try
        {
            await page.Locator("button.btn-login").ClickAsync();
            await page.WaitForURLAsync(u => !u.Contains("/TenantPortal/Login"), new() { Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
            await page.EvaluateAsync("() => document.getElementById('civilId').closest('form').submit()");
            await page.WaitForURLAsync(u => !u.Contains("/TenantPortal/Login"), new() { Timeout = 15_000 });
        }
        page.Url.Should().NotContain("/TenantPortal/Login", "tenant must sign in");

        // 2. Open the KNET payment CARD page (the test-mode gateway screen).
        await page.GotoAsync(cardUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        (await page.ContentAsync()).ToLowerInvariant().Should().ContainAny("card", "payment", "knet");
        await page.ScreenshotAsync(new() { Path = "knet-card-page.png", FullPage = true });
        System.Console.WriteLine($"KNET_CARD_PAGE_SHOWN invoice={invoiceId} amount=175.000 url={cardUrl}");

        // 3. Click "Complete Payment" (green success button → completePayment(true)).
        await page.Locator("button.btn-success").First.ClickAsync();
        await page.WaitForTimeoutAsync(4000);

        // 4. Verify settlement.
        var paymentId = await WorkflowSupport.Scalar<int>(
            "SELECT ISNULL((SELECT TOP 1 PaymentId FROM Payments WHERE InvoiceId=@i AND PaymentType=1 ORDER BY PaymentId DESC),0)", ("@i", invoiceId));
        (await WorkflowSupport.Scalar<int>("SELECT Status FROM Payments WHERE PaymentId=@p", ("@p", paymentId)))
            .Should().Be(2, "completing the KNET card → Payment Completed(2)");
        (await WorkflowSupport.Scalar<string>("SELECT Status FROM Invoices WHERE InvoiceId=@i", ("@i", invoiceId)))
            .Should().Be("Paid", "a full KNET card payment flips the invoice to Paid");

        System.Console.WriteLine($"KNET_CARD_OK tenant={tenantId} civilId={civilId} payment={paymentId} invoice={invoiceId} amount=175.000 -> PAID");
    }
}
