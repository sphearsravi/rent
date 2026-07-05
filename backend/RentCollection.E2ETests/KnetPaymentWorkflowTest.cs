using System.Text.Json;
using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// Workflow variant: settle the rent invoice via the KNET card path instead of
/// cash. KNET auto-approves on gateway success (no PaymentApproval step), so a
/// successful completion should flip the invoice straight to Paid.
/// Uses the test-only /api/payments/knet/test/complete simulator.
/// </summary>
[Collection("WorkflowSerial")]
public sealed class KnetPaymentWorkflowTest : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public KnetPaymentWorkflowTest(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Knet_success_marks_invoice_paid_and_exposes_receipt()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;

        WorkflowTree tree = default;
        try
        {
            tree = await WorkflowScenario.BuildToInvoiceAsync(page, ctx);
            tree.InvoiceId.Should().BeGreaterThan(0, "the setup must produce a rent invoice");

            // Initiate KNET (AllowAnonymous JSON endpoint; creates a Pending payment).
            var initiate = await ctx.APIRequest.PostAsync("/api/payments/knet/initiate", new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string> { ["content-type"] = "application/json" },
                Data = JsonSerializer.Serialize(new
                {
                    invoiceId = tree.InvoiceId,
                    tenantId = tree.TenantId,
                    propertyId = tree.PropertyId,
                    leaseId = tree.LeaseId,
                    amount = tree.InvoiceAmount,
                }),
            });
            initiate.Status.Should().BeInRange(200, 299, "KNET initiate must succeed");

            var knetPaymentId = await WorkflowSupport.Scalar<int>(
                "SELECT ISNULL((SELECT TOP 1 PaymentId FROM Payments WHERE TenantId=@t AND PaymentType=1 ORDER BY PaymentId DESC),0)",
                ("@t", tree.TenantId));
            knetPaymentId.Should().BeGreaterThan(0, "initiate must create a KNET payment row");

            // Simulate a successful gateway completion.
            var complete = await ctx.APIRequest.PostAsync("/api/payments/knet/test/complete", new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string> { ["content-type"] = "application/json" },
                Data = JsonSerializer.Serialize(new { paymentId = knetPaymentId, success = true }),
            });
            complete.Status.Should().BeInRange(200, 299, "KNET test-complete must succeed");

            (await WorkflowSupport.Scalar<int>("SELECT Status FROM Payments WHERE PaymentId=@p", ("@p", knetPaymentId)))
                .Should().Be(2, "KNET success → Payment.Completed(2), auto-approved");
            (await WorkflowSupport.Scalar<string>("SELECT Status FROM Invoices WHERE InvoiceId=@i", ("@i", tree.InvoiceId)))
                .Should().Be("Paid", "a full KNET payment flips the invoice to Paid without an approval step");

            var receipt = await ctx.APIRequest.GetAsync($"/api/payments/{knetPaymentId}/receipt.pdf");
            receipt.Status.Should().Be(200);
            (receipt.Headers.TryGetValue("content-type", out var t) ? t : "").Should().Contain("pdf");

            Console.WriteLine($"KNET_OK invoice={tree.InvoiceId} payment={knetPaymentId} paid+receipt");
        }
        finally
        {
            if (tree.TenantId > 0)
                await WorkflowSupport.TeardownTree(tree.TenantId, tree.LeaseId, tree.PropertyId);
        }
    }
}
