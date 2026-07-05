using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// Adversarial regression tests for the payment-approval state-machine breaks
/// the red-team council found. Each asserts the SAFE invariant, so it FAILS
/// today (proving the vulnerability) and will PASS once the guards are added.
/// </summary>
[Collection("WorkflowSerial")]
public sealed class PaymentApprovalVulnTests : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public PaymentApprovalVulnTests(E2EFixture fixture) => _fixture = fixture;

    /// <summary>Record a full cash payment for the tree's invoice; return its PaymentId.</summary>
    private static async Task<int> RecordCashAsync(IPage page, WorkflowTree tree)
    {
        await page.GotoAsync("/CashPayment/Record", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("#InvoiceId").SelectOptionAsync(new SelectOptionValue { Value = tree.InvoiceId.ToString() });
        await page.Locator("#Amount").FillAsync(tree.InvoiceAmount.ToString("0.000"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Record" }).First.ClickAsync(new() { NoWaitAfter = true });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        return await WorkflowSupport.Scalar<int>(
            "SELECT ISNULL((SELECT TOP 1 PaymentId FROM Payments WHERE TenantId=@t ORDER BY PaymentId DESC),0)", ("@t", tree.TenantId));
    }

    // #1 — Approve must be idempotent: re-approving must NOT over-credit the invoice.
    [Fact]
    public async Task Double_approve_must_not_overcredit_invoice()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        WorkflowTree tree = default;
        try
        {
            tree = await WorkflowScenario.BuildToInvoiceAsync(page, ctx);
            var pid = await RecordCashAsync(page, tree);

            await WorkflowSupport.PostForm(ctx, $"/PaymentApproval/Approve/{pid}");
            await WorkflowSupport.PostForm(ctx, $"/PaymentApproval/Approve/{pid}");  // replay
            await WorkflowSupport.PostForm(ctx, $"/PaymentApproval/Approve/{pid}");  // replay again

            var paid = await WorkflowSupport.Scalar<decimal>("SELECT PaidAmount FROM Invoices WHERE InvoiceId=@i", ("@i", tree.InvoiceId));
            Console.WriteLine($"VULN1 invoice={tree.InvoiceId} total={tree.InvoiceAmount} paidAfter3Approvals={paid}");
            paid.Should().BeLessThanOrEqualTo(tree.InvoiceAmount,
                "re-approving one payment must never credit more than the invoice total");
        }
        finally { if (tree.TenantId > 0) await WorkflowSupport.TeardownTree(tree.TenantId, tree.LeaseId, tree.PropertyId); }
    }

    // #2 — A REJECTED payment must not be resurrectable via Approve.
    [Fact]
    public async Task Approving_a_rejected_payment_must_be_blocked()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        WorkflowTree tree = default;
        try
        {
            tree = await WorkflowScenario.BuildToInvoiceAsync(page, ctx);
            var pid = await RecordCashAsync(page, tree);

            await WorkflowSupport.PostForm(ctx, $"/PaymentApproval/Reject/{pid}", new[] { ("reason", "vuln-test") });
            await WorkflowSupport.PostForm(ctx, $"/PaymentApproval/Approve/{pid}");   // try to resurrect

            var status = await WorkflowSupport.Scalar<int>("SELECT Status FROM Payments WHERE PaymentId=@p", ("@p", pid));
            var invStatus = await WorkflowSupport.Scalar<string>("SELECT Status FROM Invoices WHERE InvoiceId=@i", ("@i", tree.InvoiceId));
            Console.WriteLine($"VULN2 payment={pid} statusAfterRejectThenApprove={status} invoice={invStatus}");
            status.Should().Be(7, "a rejected payment must stay Rejected(7) — Approve must not resurrect it");
            invStatus.Should().NotBe("Paid", "a rejected payment must not mark the invoice Paid");
        }
        finally { if (tree.TenantId > 0) await WorkflowSupport.TeardownTree(tree.TenantId, tree.LeaseId, tree.PropertyId); }
    }

    // #3 — Rejecting an already-COMPLETED payment must not leave the invoice Paid with no valid payment.
    [Fact]
    public async Task Rejecting_a_completed_payment_must_not_leave_invoice_paid()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        WorkflowTree tree = default;
        try
        {
            tree = await WorkflowScenario.BuildToInvoiceAsync(page, ctx);
            var pid = await RecordCashAsync(page, tree);

            await WorkflowSupport.PostForm(ctx, $"/PaymentApproval/Approve/{pid}");   // invoice → Paid
            await WorkflowSupport.PostForm(ctx, $"/PaymentApproval/Reject/{pid}", new[] { ("reason", "vuln-test") });

            var status = await WorkflowSupport.Scalar<int>("SELECT Status FROM Payments WHERE PaymentId=@p", ("@p", pid));
            var invStatus = await WorkflowSupport.Scalar<string>("SELECT Status FROM Invoices WHERE InvoiceId=@i", ("@i", tree.InvoiceId));
            Console.WriteLine($"VULN3 payment={pid} statusAfterApproveThenReject={status} invoice={invStatus}");
            // Safe outcome is EITHER: reject was blocked (payment stays Completed=2),
            // OR it was reversed (invoice no longer Paid). The bug is Rejected(7) + still Paid.
            (status == 2 || invStatus != "Paid").Should()
                .BeTrue("rejecting a completed payment must not leave a Paid invoice with a Rejected payment behind it");
        }
        finally { if (tree.TenantId > 0) await WorkflowSupport.TeardownTree(tree.TenantId, tree.LeaseId, tree.PropertyId); }
    }
}
