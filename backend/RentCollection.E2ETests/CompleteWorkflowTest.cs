using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// THE complete end-to-end business workflow (cash path), driven through a real
/// browser and asserted against the DATABASE at every hop:
///
///   Login → Property → Unit → Tenant → Lease(Active) → Generate Invoice →
///   Record Cash Payment → Approve → Invoice Paid → Receipt PDF
///
/// Shares provisioning with the KNET/reject variants via WorkflowScenario, and
/// tears the tree down in reverse-FK order. Watch it live:
///   RC_E2E_HEADED=1 RC_E2E_SLOWMO=1200 dotnet test --filter CompleteWorkflowTest
/// </summary>
[Collection("WorkflowSerial")]
public sealed class CompleteWorkflowTest : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public CompleteWorkflowTest(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Full_business_workflow_property_to_receipt()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;

        WorkflowTree tree = default;
        try
        {
            // Steps 1–5: Property → Unit → Tenant → Lease(Active) → Invoice.
            tree = await WorkflowScenario.BuildToInvoiceAsync(page, ctx);
            tree.PropertyId.Should().BeGreaterThan(0, "property must be created");
            tree.UnitId.Should().BeGreaterThan(0, "unit must be created");
            tree.TenantId.Should().BeGreaterThan(0, "tenant must be created");
            tree.LeaseId.Should().BeGreaterThan(0, "lease must be created");
            tree.InvoiceId.Should().BeGreaterThan(0, "a rent invoice must be generated");
            (await WorkflowSupport.Scalar<string>("SELECT Status FROM Leases WHERE LeaseId=@l", ("@l", tree.LeaseId)))
                .Should().Be("Active", "deposit=0 lease lands Active");
            (await WorkflowSupport.Scalar<string>("SELECT Status FROM Units WHERE UnitId=@u", ("@u", tree.UnitId)))
                .Should().Be("Occupied", "an active lease flips the unit to Occupied");

            // 6. Cash payment (UI → PendingVerification).
            await page.GotoAsync("/CashPayment/Record", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await page.Locator("#InvoiceId").SelectOptionAsync(new SelectOptionValue { Value = tree.InvoiceId.ToString() });
            await page.Locator("#Amount").FillAsync(tree.InvoiceAmount.ToString("0.000"));
            await page.GetByRole(AriaRole.Button, new() { Name = "Record" }).First.ClickAsync(new() { NoWaitAfter = true });
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            var paymentId = await WorkflowSupport.Scalar<int>(
                "SELECT ISNULL((SELECT TOP 1 PaymentId FROM Payments WHERE TenantId=@t ORDER BY PaymentId DESC),0)", ("@t", tree.TenantId));
            paymentId.Should().BeGreaterThan(0, "a payment record must be created");

            // 7. Approve (→ Completed, invoice → Paid).
            (await WorkflowSupport.PostForm(ctx, $"/PaymentApproval/Approve/{paymentId}")).Should().BeInRange(200, 399);
            (await WorkflowSupport.Scalar<int>("SELECT Status FROM Payments WHERE PaymentId=@p", ("@p", paymentId)))
                .Should().Be(2, "approved payment → Completed(2)");
            (await WorkflowSupport.Scalar<string>("SELECT Status FROM Invoices WHERE InvoiceId=@i", ("@i", tree.InvoiceId)))
                .Should().Be("Paid", "full payment flips the invoice to Paid");

            // 8. Receipt PDF.
            var receipt = await ctx.APIRequest.GetAsync($"/api/payments/{paymentId}/receipt.pdf");
            receipt.Status.Should().Be(200, "a completed payment must expose a receipt PDF");
            (receipt.Headers.TryGetValue("content-type", out var ctype) ? ctype : "").Should().Contain("pdf");

            Console.WriteLine($"WORKFLOW_OK property={tree.PropertyId} unit={tree.UnitId} tenant={tree.TenantId} " +
                $"lease={tree.LeaseId} invoice={tree.InvoiceId} payment={paymentId} approved+paid receipt=200/pdf");
        }
        finally
        {
            if (tree.TenantId > 0 || tree.PropertyId > 0)
                await WorkflowSupport.TeardownTree(tree.TenantId, tree.LeaseId, tree.PropertyId);
        }
    }
}
