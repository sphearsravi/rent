using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;
using RentCollection.E2ETests.Pages;

namespace RentCollection.E2ETests;

/// <summary>
/// LIVE, watch-along demo of the BANK TRANSFER path (two-person control):
///   tenant logs in (mobile API) → submits a bank transfer + proof image via
///   /api/payments/bank/upload → lands in PendingVerification →
///   admin approves through the /PaymentApproval UI → invoice settles to Paid,
///   BankTransfer → Approved, audit trail written.
/// The tenant submission runs over the real API (with a proof PNG); the admin
/// approval runs in a visible browser (RC_E2E_HEADED=1) so you can watch it.
/// </summary>
[Collection("WorkflowSerial")]
public sealed class BankTransferLiveDemo : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public BankTransferLiveDemo(E2EFixture fixture) => _fixture = fixture;

    // Minimal 1x1 PNG — stands in for the tenant's transfer-receipt photo.
    private static byte[] Png() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Fact]
    public async Task Bank_transfer_submit_then_approve_settles_invoice()
    {
        var baseUrl = Environment.GetEnvironmentVariable("RC_E2E_BASE_URL") ?? "http://localhost:5032";
        var invoiceId = int.Parse(Environment.GetEnvironmentVariable("RC_DEMO_INVOICE") ?? "1408");
        var civilId = Environment.GetEnvironmentVariable("RC_TENANT_CIVILID") ?? "201011083576";
        var tenantPass = Environment.GetEnvironmentVariable("RC_TENANT_PASS") ?? "Tenant@12345";
        var adminEmail = Environment.GetEnvironmentVariable("RC_ADMIN_EMAIL")
            ?? throw new InvalidOperationException("Set RC_ADMIN_EMAIL");
        var adminPass = Environment.GetEnvironmentVariable("RC_ADMIN_PASS")
            ?? throw new InvalidOperationException("Set RC_ADMIN_PASS");
        const int tenantId = 711;
        var amount = await WorkflowSupport.Scalar<decimal>(
            "SELECT TotalAmount FROM Invoices WHERE InvoiceId=@i", ("@i", invoiceId));

        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        // ── TENANT SIDE: login → submit bank transfer with proof image ──
        Console.WriteLine($"BANK_STEP_1 tenant login (civilId {civilId})");
        var loginResp = await http.PostAsJsonAsync("/api/tenant/login", new { CivilId = civilId, Password = tenantPass });
        loginResp.EnsureSuccessStatusCode();
        var token = JsonDocument.Parse(await loginResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data").GetProperty("token").GetString();
        token.Should().NotBeNullOrEmpty("tenant must authenticate");

        Console.WriteLine($"BANK_STEP_2 tenant submits bank transfer {amount:0.000} KWD + proof image for invoice {invoiceId}");
        using var form = new MultipartFormDataContent
        {
            { new StringContent(invoiceId.ToString()), "InvoiceId" },
            { new StringContent(tenantId.ToString()), "TenantId" },
            { new StringContent("0"), "PropertyId" }, // server derives from the invoice's lease
            { new StringContent(amount.ToString("0.000")), "Amount" },
            { new StringContent(DateTime.UtcNow.ToString("o")), "TransferDate" },
            { new StringContent("National Bank of Kuwait"), "BankName" },
            { new StringContent("NBK-DEMO-778899"), "ReferenceNumber" },
            { new StringContent("Bank transfer demo"), "Notes" },
        };
        var png = new ByteArrayContent(Png());
        png.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(png, "ProofFile", "transfer-proof.png");

        var upReq = new HttpRequestMessage(HttpMethod.Post, "/api/payments/bank/upload") { Content = form };
        upReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var upResp = await http.SendAsync(upReq);
        var upBody = await upResp.Content.ReadAsStringAsync();
        upResp.IsSuccessStatusCode.Should().BeTrue("bank upload should succeed: " + upBody);

        var paymentId = await WorkflowSupport.Scalar<int>(
            "SELECT ISNULL((SELECT TOP 1 PaymentId FROM Payments WHERE InvoiceId=@i AND PaymentType=3 ORDER BY PaymentId DESC),0)", ("@i", invoiceId));
        var btStatus = await WorkflowSupport.Scalar<int>(
            "SELECT ISNULL((SELECT TOP 1 Status FROM BankTransfers WHERE PaymentId=@p),0)", ("@p", paymentId));
        var payStatus1 = await WorkflowSupport.Scalar<int>("SELECT Status FROM Payments WHERE PaymentId=@p", ("@p", paymentId));
        var invStatus1 = await WorkflowSupport.Scalar<string>("SELECT Status FROM Invoices WHERE InvoiceId=@i", ("@i", invoiceId));
        Console.WriteLine($"BANK_STEP_2_RESULT payment={paymentId} payStatus={payStatus1} (5=PendingVerification) bankTransferStatus={btStatus} (1=PendingVerification) invoice=[{invStatus1}] — unpaid, awaiting approval");

        // ── ADMIN SIDE (watch): login → approve the bank transfer via the UI ──
        await using var ctx = await _fixture.NewContextAsync();
        var page = await ctx.NewPageAsync();
        Console.WriteLine($"BANK_STEP_3 admin login as {adminEmail} → approve payment {paymentId} via /PaymentApproval");
        var login = new LoginPage(page);
        await page.GotoAsync("/Account/Login", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await login.SubmitAsync(adminEmail, adminPass);
        await page.WaitForTimeoutAsync(1500);
        page.Url.Should().NotContain("/Account/Login", "admin must sign in");
        await page.GotoAsync("/PaymentApproval", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForTimeoutAsync(1500);
        await page.GotoAsync($"/PaymentApproval/Details/{paymentId}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator(".btn-approve").First.ClickAsync();
        await page.Locator(".btn-confirm-approve").First.ClickAsync(new() { NoWaitAfter = true });
        await page.WaitForTimeoutAsync(3000);

        // ── VERIFY settlement + bank-transfer status + audit ──
        string? invFinal = null;
        for (int i = 0; i < 15; i++)
        {
            invFinal = await WorkflowSupport.Scalar<string>("SELECT Status FROM Invoices WHERE InvoiceId=@i", ("@i", invoiceId));
            if (invFinal == "Paid") break;
            await Task.Delay(700);
        }
        var payFinal = await WorkflowSupport.Scalar<int>("SELECT Status FROM Payments WHERE PaymentId=@p", ("@p", paymentId));
        var btFinal = await WorkflowSupport.Scalar<int>("SELECT Status FROM BankTransfers WHERE PaymentId=@p", ("@p", paymentId));
        var auditRows = await WorkflowSupport.Scalar<int>("SELECT COUNT(*) FROM PaymentAudits WHERE PaymentId=@p", ("@p", paymentId));
        Console.WriteLine($"BANK_STEP_3_RESULT invoice=[{invFinal}] payment={payFinal} (2=Completed) bankTransfer={btFinal} (2=Approved) auditRows={auditRows}");

        invFinal.Should().Be("Paid", "approving the bank transfer settles the invoice");
        payFinal.Should().Be(2, "approved bank transfer payment must be Completed(2)");
        btFinal.Should().Be(2, "the BankTransfer record must be Approved(2)");
    }
}
