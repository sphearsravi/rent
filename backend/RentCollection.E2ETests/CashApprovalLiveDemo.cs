using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;
using RentCollection.E2ETests.Pages;

namespace RentCollection.E2ETests;

/// <summary>
/// LIVE, watch-along demo of the two-person-control cash path:
///   admin login → /CashPayment/Record → payment lands in PendingVerification
///   → /PaymentApproval Details UI → Approve → invoice settles to Paid + audit row.
/// Run headed:
///   RC_E2E_BASE_URL=https://www.alrtam.co RC_E2E_DB='…' RC_E2E_HEADED=1 RC_E2E_SLOWMO=600 \
///   RC_ADMIN_EMAIL=… RC_ADMIN_PASS=… RC_DEMO_INVOICE=1406 \
///   dotnet test --filter FullyQualifiedName~CashApprovalLiveDemo
/// </summary>
[Collection("WorkflowSerial")]
public sealed class CashApprovalLiveDemo : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public CashApprovalLiveDemo(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Cash_record_then_approve_settles_invoice()
    {
        var invoiceId = int.Parse(Environment.GetEnvironmentVariable("RC_DEMO_INVOICE") ?? "1406");
        var email = Environment.GetEnvironmentVariable("RC_ADMIN_EMAIL")
            ?? throw new InvalidOperationException("Set RC_ADMIN_EMAIL");
        var pass = Environment.GetEnvironmentVariable("RC_ADMIN_PASS")
            ?? throw new InvalidOperationException("Set RC_ADMIN_PASS");
        var amount = await WorkflowSupport.Scalar<decimal>(
            "SELECT TotalAmount FROM Invoices WHERE InvoiceId=@i", ("@i", invoiceId));

        await using var ctx = await _fixture.NewContextAsync();
        var page = await ctx.NewPageAsync();

        // 1. Admin login.
        Console.WriteLine($"CASH_STEP_1 admin login as {email}");
        var login = new LoginPage(page);
        await page.GotoAsync("/Account/Login", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await login.SubmitAsync(email, pass);
        await page.WaitForTimeoutAsync(1500);
        page.Url.Should().NotContain("/Account/Login",
            "admin must sign in — check RC_ADMIN_EMAIL/RC_ADMIN_PASS");

        // 2. Record cash → lands in PendingVerification (NOT settled yet).
        Console.WriteLine($"CASH_STEP_2 record cash {amount:0.000} KWD for invoice {invoiceId}");
        await page.GotoAsync("/CashPayment/Record", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("#InvoiceId").SelectOptionAsync(new SelectOptionValue { Value = invoiceId.ToString() });
        await page.Locator("#Amount").FillAsync(amount.ToString("0.000"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Record" }).First.ClickAsync(new() { NoWaitAfter = true });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var paymentId = await WorkflowSupport.Scalar<int>(
            "SELECT ISNULL((SELECT TOP 1 PaymentId FROM Payments WHERE InvoiceId=@i AND PaymentType=2 ORDER BY PaymentId DESC),0)",
            ("@i", invoiceId));
        var recStatus = await WorkflowSupport.Scalar<int>("SELECT Status FROM Payments WHERE PaymentId=@p", ("@p", paymentId));
        var invAfterRecord = await WorkflowSupport.Scalar<string>("SELECT Status FROM Invoices WHERE InvoiceId=@i", ("@i", invoiceId));
        Console.WriteLine($"CASH_STEP_2_RESULT payment={paymentId} status={recStatus} (5=PendingVerification) invoice=[{invAfterRecord}] (still unpaid — awaiting approval)");

        // 3. Show it waiting in the approval queue.
        await page.GotoAsync("/PaymentApproval", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForTimeoutAsync(1500);

        // 4. Approve through the real Details UI (second person).
        Console.WriteLine($"CASH_STEP_3 approve payment {paymentId} via /PaymentApproval/Details");
        await page.GotoAsync($"/PaymentApproval/Details/{paymentId}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator(".btn-approve").First.ClickAsync();
        await page.Locator(".btn-confirm-approve").First.ClickAsync(new() { NoWaitAfter = true });
        await page.WaitForTimeoutAsync(3000);

        // 5. Verify settlement + audit trail.
        string? invStatus = null;
        for (int i = 0; i < 15; i++)
        {
            invStatus = await WorkflowSupport.Scalar<string>("SELECT Status FROM Invoices WHERE InvoiceId=@i", ("@i", invoiceId));
            if (invStatus == "Paid") break;
            await Task.Delay(700);
        }
        var payStatus = await WorkflowSupport.Scalar<int>("SELECT Status FROM Payments WHERE PaymentId=@p", ("@p", paymentId));
        var auditRows = await WorkflowSupport.Scalar<int>("SELECT COUNT(*) FROM PaymentAudits WHERE PaymentId=@p", ("@p", paymentId));
        Console.WriteLine($"CASH_STEP_3_RESULT invoice=[{invStatus}] payment={payStatus} (2=Completed) auditRows={auditRows}");

        invStatus.Should().Be("Paid", "approving the cash payment settles the invoice");
        payStatus.Should().Be(2, "approved cash payment must be Completed(2)");
    }
}
