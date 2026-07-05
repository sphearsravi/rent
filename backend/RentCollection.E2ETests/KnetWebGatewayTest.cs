using System.Text.Json;
using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// LIVE: initiates a fresh KNET payment for the tenant's oldest outstanding
/// invoice, opens the REAL KNET test gateway (kpaytest.com.kw), fills the test
/// card 8888 8800 0000 0001 / 09/30 / 1234, submits, and watches the callback.
/// Watch: RC_E2E_HEADED=1 RC_E2E_SLOWMO=600 dotnet test --filter KnetWebGatewayTest
/// </summary>
[Collection("WorkflowSerial")]
public sealed class KnetWebGatewayTest : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public KnetWebGatewayTest(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Pay_on_real_knet_gateway()
    {
        var baseUrl = Environment.GetEnvironmentVariable("RC_E2E_BASE_URL")!;
        var tenantId = 711; var propertyId = 67; var leaseId = 693;
        // oldest outstanding invoice for the tenant
        var invoiceId = await WorkflowSupport.Scalar<int>(
            "SELECT TOP 1 InvoiceId FROM Invoices WHERE TenantId=@t AND Status NOT IN ('Paid','Cancelled') ORDER BY InvoiceDate, InvoiceId", ("@t", tenantId));
        var amount = await WorkflowSupport.Scalar<decimal>("SELECT TotalAmount FROM Invoices WHERE InvoiceId=@i", ("@i", invoiceId));

        await using var ctx = await _fixture.NewContextAsync();
        var page = await ctx.NewPageAsync();

        // Fresh initiate → fresh, single-use gateway URL.
        var init = await ctx.APIRequest.PostAsync($"{baseUrl}/api/payments/knet/initiate", new()
        {
            Headers = new Dictionary<string, string> { ["content-type"] = "application/json" },
            Data = JsonSerializer.Serialize(new { invoiceId, tenantId, propertyId, leaseId, amount }),
        });
        var j = await init.JsonAsync();
        j.Value.GetProperty("success").GetBoolean().Should().BeTrue("initiate: " + await init.TextAsync());
        var url = j.Value.GetProperty("redirectUrl").GetString()!;
        System.Console.WriteLine($"KNET_INIT invoice={invoiceId} amount={amount:0.000}");

        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 40_000 });
        await page.WaitForTimeoutAsync(3000);
        System.Console.WriteLine($"KNET_LANDED url=[{page.Url}]");
        await page.ScreenshotAsync(new() { Path = "knet-web-form.png", FullPage = true });

        var dump = await page.EvaluateAsync<string>(@"() => JSON.stringify(
            [...document.querySelectorAll('input,select')].map(e => ({ name:e.name, id:e.id, type:e.type, ph:e.placeholder })))");
        System.Console.WriteLine("KNET_INPUTS " + dump);

        // Real KNET card page — fill the exact fields (discovered from the dump).
        if (page.Url.Contains("kpaytest"))
        {
            var expiry = Environment.GetEnvironmentVariable("RC_KNET_EXPIRY") ?? "09/30";
            await page.FillAsync("#debitNumber", "8888880000000001");
            await page.FillAsync("#expiryDateInput", expiry);
            await page.FillAsync("#cardPin", "1234");
            System.Console.WriteLine($"KNET_EXPIRY_USED {expiry}");
            await page.WaitForTimeoutAsync(600);
            await page.ScreenshotAsync(new() { Path = "knet-web-filled.png", FullPage = true });
            System.Console.WriteLine("KNET_FILLED card+exp+pin");

            // Click PROCEED → confirmation screen.
            await page.ClickAsync("#proceed");
            await page.WaitForTimeoutAsync(5000);
            await page.ScreenshotAsync(new() { Path = "knet-web-proceed.png", FullPage = true });
            System.Console.WriteLine($"KNET_AFTER_PROCEED url=[{page.Url}]");

            // Click CONFIRM on the confirmation screen to finalize the charge.
            var confirm = page.Locator("#proceedConfirm");
            if (await confirm.CountAsync() > 0)
            {
                await confirm.ClickAsync(new() { Force = true });
                System.Console.WriteLine("KNET_CONFIRM clicked");
                await page.WaitForTimeoutAsync(6000);
                await page.ScreenshotAsync(new() { Path = "knet-web-confirmed.png", FullPage = true });
            }

            // Handle an OTP/3DS step if it appears (test OTP).
            var otp = page.Locator("#debitOTP");
            if (await otp.CountAsync() > 0 && await otp.IsVisibleAsync())
            {
                try { await otp.FillAsync("123456"); } catch { }
                System.Console.WriteLine("KNET_OTP entered");
                var oc = page.Locator("#proceedConfirm, #proceed");
                try { await oc.First.ClickAsync(new() { Force = true }); } catch { }
                await page.WaitForTimeoutAsync(8000);
            }

            // Wait for the gateway to redirect back to our site (callback settles).
            try { await page.WaitForURLAsync(u => u.Contains("alrtam.co"), new() { Timeout = 25_000 }); } catch { }
            await page.WaitForTimeoutAsync(3000);
            await page.ScreenshotAsync(new() { Path = "knet-web-result.png", FullPage = true });
        }

        var invStatus = await WorkflowSupport.Scalar<string>("SELECT Status FROM Invoices WHERE InvoiceId=@i", ("@i", invoiceId));
        var payStatus = await WorkflowSupport.Scalar<int>(
            "SELECT ISNULL((SELECT TOP 1 Status FROM Payments WHERE InvoiceId=@i AND PaymentType=1 ORDER BY PaymentId DESC),0)", ("@i", invoiceId));
        System.Console.WriteLine($"KNET_WEB_AFTER url=[{page.Url}]");
        System.Console.WriteLine($"KNET_WEB_INVOICE status=[{invStatus}] paymentStatus=[{payStatus}] (2=Completed,3=Failed,1=Pending)");
    }
}
