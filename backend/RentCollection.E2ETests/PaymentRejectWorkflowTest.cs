using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// Workflow variant: record a cash payment then REJECT it in the approval
/// queue. The payment must land Rejected(7) and the invoice must NOT become
/// Paid (stays Pending) — the negative path of the approval workflow.
/// </summary>
[Collection("WorkflowSerial")]
public sealed class PaymentRejectWorkflowTest : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public PaymentRejectWorkflowTest(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Rejected_cash_payment_leaves_invoice_unpaid()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;

        WorkflowTree tree = default;
        try
        {
            tree = await WorkflowScenario.BuildToInvoiceAsync(page, ctx);

            // Record a cash payment (→ PendingVerification).
            await page.GotoAsync("/CashPayment/Record", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await page.Locator("#InvoiceId").SelectOptionAsync(new SelectOptionValue { Value = tree.InvoiceId.ToString() });
            await page.Locator("#Amount").FillAsync(tree.InvoiceAmount.ToString("0.000"));
            await page.GetByRole(AriaRole.Button, new() { Name = "Record" }).First.ClickAsync(new() { NoWaitAfter = true });
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var paymentId = await WorkflowSupport.Scalar<int>(
                "SELECT ISNULL((SELECT TOP 1 PaymentId FROM Payments WHERE TenantId=@t ORDER BY PaymentId DESC),0)", ("@t", tree.TenantId));
            paymentId.Should().BeGreaterThan(0);
            (await WorkflowSupport.Scalar<int>("SELECT Status FROM Payments WHERE PaymentId=@p", ("@p", paymentId)))
                .Should().Be(5, "a recorded cash payment starts PendingVerification(5)");

            // Reject it (reason is a required form field).
            (await WorkflowSupport.PostForm(ctx, $"/PaymentApproval/Reject/{paymentId}",
                new[] { ("reason", "E2E negative-path test") })).Should().BeInRange(200, 399);

            (await WorkflowSupport.Scalar<int>("SELECT Status FROM Payments WHERE PaymentId=@p", ("@p", paymentId)))
                .Should().Be(7, "reject → Payment.Rejected(7)");
            (await WorkflowSupport.Scalar<string>("SELECT Status FROM Invoices WHERE InvoiceId=@i", ("@i", tree.InvoiceId)))
                .Should().NotBe("Paid", "a rejected payment must not mark the invoice Paid");

            Console.WriteLine($"REJECT_OK invoice={tree.InvoiceId} payment={paymentId} rejected, invoice not paid");
        }
        finally
        {
            if (tree.TenantId > 0)
                await WorkflowSupport.TeardownTree(tree.TenantId, tree.LeaseId, tree.PropertyId);
        }
    }
}
