using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// Refund workflow (Item 8) end-to-end coverage — previously untested.
/// Exercises the real /Refunds endpoints against a live invoice+payment tree:
///   • create → approve → settle reverses the invoice PaidAmount (Partially Paid)
///   • full-amount refund reverts the invoice to Pending
///   • over-refund (amount &gt; payment.AmountPaid) is rejected — no row written
///   • settling before approval is blocked — refund stays Pending, invoice untouched
///
/// NOTE: <c>Refund.Status</c> is stored as an nvarchar string ("Pending"/
/// "Approved"/"Settled"/"Rejected") — see <c>RefundStatus</c> in
/// RentCollection.Core.Entities.Refund and the Refunds migration
/// (Status = nvarchar(max)). It is NOT an int enum, so we assert on the strings.
/// All scenarios reuse WorkflowScenario, verify against the DB, and self-clean.
/// </summary>
[Collection("WorkflowSerial")]
public sealed class RefundsTests : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public RefundsTests(E2EFixture fixture) => _fixture = fixture;

    /// <summary>Record a cash payment for the tree's invoice via the UI; return its PaymentId.</summary>
    private static async Task<int> RecordCashAsync(IPage page, WorkflowTree tree, decimal amount)
    {
        await page.GotoAsync("/CashPayment/Record", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("#InvoiceId").SelectOptionAsync(new SelectOptionValue { Value = tree.InvoiceId.ToString() });
        await page.Locator("#Amount").FillAsync(amount.ToString("0.000"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Record" }).First.ClickAsync(new() { NoWaitAfter = true });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        return await WorkflowSupport.Scalar<int>(
            "SELECT ISNULL((SELECT TOP 1 PaymentId FROM Payments WHERE TenantId=@t ORDER BY PaymentId DESC),0)", ("@t", tree.TenantId));
    }

    /// <summary>Newest RefundId for a payment, or 0 if none.</summary>
    private static Task<int> LatestRefundIdAsync(int paymentId) => WorkflowSupport.Scalar<int>(
        "SELECT ISNULL((SELECT TOP 1 RefundId FROM Refunds WHERE PaymentId=@p ORDER BY RefundId DESC),0)", ("@p", paymentId));

    /// <summary>Record a full-invoice cash payment and approve it → invoice Paid, PaidAmount=InvoiceAmount.</summary>
    private static async Task<int> RecordAndApproveFullAsync(IPage page, IBrowserContext ctx, WorkflowTree tree)
    {
        var paymentId = await RecordCashAsync(page, tree, tree.InvoiceAmount);
        (await WorkflowSupport.PostForm(ctx, $"/PaymentApproval/Approve/{paymentId}")).Should().BeInRange(200, 399);
        (await WorkflowSupport.Scalar<string>("SELECT Status FROM Invoices WHERE InvoiceId=@i", ("@i", tree.InvoiceId)))
            .Should().Be("Paid");
        (await WorkflowSupport.Scalar<decimal>("SELECT PaidAmount FROM Invoices WHERE InvoiceId=@i", ("@i", tree.InvoiceId)))
            .Should().Be(tree.InvoiceAmount);
        return paymentId;
    }

    [Fact]
    public async Task Refund_full_lifecycle_reverses_invoice_paidamount()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        WorkflowTree tree = default;
        try
        {
            tree = await WorkflowScenario.BuildToInvoiceAsync(page, ctx);
            var paymentId = await RecordAndApproveFullAsync(page, ctx, tree);

            // Refund half of the paid amount.
            var refundAmount = Math.Round(tree.InvoiceAmount / 2, 3);
            (await WorkflowSupport.PostForm(ctx, "/Refunds/Create", new[]
            {
                ("paymentId", paymentId.ToString()),
                ("amount", refundAmount.ToString("0.000")),
                ("method", "Cash"),
                ("reason", "partial refund"),
                ("referenceNumber", "REF1"),
            }, $"/Refunds/Create?paymentId={paymentId}")).Should().BeInRange(200, 399);

            var refundId = await LatestRefundIdAsync(paymentId);
            refundId.Should().BeGreaterThan(0, "the refund row must have been created");
            (await WorkflowSupport.Scalar<string>("SELECT Status FROM Refunds WHERE RefundId=@r", ("@r", refundId)))
                .Should().Be("Pending");

            // Approve → Settle.
            (await WorkflowSupport.PostForm(ctx, $"/Refunds/Approve/{refundId}")).Should().BeInRange(200, 399);
            (await WorkflowSupport.Scalar<string>("SELECT Status FROM Refunds WHERE RefundId=@r", ("@r", refundId)))
                .Should().Be("Approved");
            (await WorkflowSupport.PostForm(ctx, $"/Refunds/Settle/{refundId}")).Should().BeInRange(200, 399);

            // Settled refund reverses the invoice.
            (await WorkflowSupport.Scalar<string>("SELECT Status FROM Refunds WHERE RefundId=@r", ("@r", refundId)))
                .Should().Be("Settled", "the refund must be Settled after Settle");
            (await WorkflowSupport.Scalar<decimal>("SELECT PaidAmount FROM Invoices WHERE InvoiceId=@i", ("@i", tree.InvoiceId)))
                .Should().Be(tree.InvoiceAmount - refundAmount, "settling reduces the invoice PaidAmount by the refund amount");
            (await WorkflowSupport.Scalar<string>("SELECT Status FROM Invoices WHERE InvoiceId=@i", ("@i", tree.InvoiceId)))
                .Should().Be("Partially Paid", "a partial refund flips a Paid invoice back to Partially Paid");

            Console.WriteLine($"REFUND_LIFECYCLE_OK refund={refundId} invoice={tree.InvoiceId} paid {tree.InvoiceAmount}→{tree.InvoiceAmount - refundAmount} (Partially Paid)");
        }
        finally { if (tree.TenantId > 0) await WorkflowSupport.TeardownTree(tree.TenantId, tree.LeaseId, tree.PropertyId); }
    }

    [Fact]
    public async Task Refund_full_amount_reverts_invoice_to_Pending()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        WorkflowTree tree = default;
        try
        {
            tree = await WorkflowScenario.BuildToInvoiceAsync(page, ctx);
            var paymentId = await RecordAndApproveFullAsync(page, ctx, tree);

            // Refund the entire paid amount.
            (await WorkflowSupport.PostForm(ctx, "/Refunds/Create", new[]
            {
                ("paymentId", paymentId.ToString()),
                ("amount", tree.InvoiceAmount.ToString("0.000")),
                ("method", "Cash"),
                ("reason", "full refund"),
                ("referenceNumber", "REF1"),
            }, $"/Refunds/Create?paymentId={paymentId}")).Should().BeInRange(200, 399);

            var refundId = await LatestRefundIdAsync(paymentId);
            refundId.Should().BeGreaterThan(0, "the refund row must have been created");

            (await WorkflowSupport.PostForm(ctx, $"/Refunds/Approve/{refundId}")).Should().BeInRange(200, 399);
            (await WorkflowSupport.PostForm(ctx, $"/Refunds/Settle/{refundId}")).Should().BeInRange(200, 399);

            (await WorkflowSupport.Scalar<string>("SELECT Status FROM Refunds WHERE RefundId=@r", ("@r", refundId)))
                .Should().Be("Settled");
            (await WorkflowSupport.Scalar<decimal>("SELECT PaidAmount FROM Invoices WHERE InvoiceId=@i", ("@i", tree.InvoiceId)))
                .Should().Be(0m, "a full refund zeroes the invoice PaidAmount");
            (await WorkflowSupport.Scalar<string>("SELECT Status FROM Invoices WHERE InvoiceId=@i", ("@i", tree.InvoiceId)))
                .Should().Be("Pending", "a full refund reverts the invoice to Pending");

            Console.WriteLine($"REFUND_FULL_OK refund={refundId} invoice={tree.InvoiceId} PaidAmount→0 (Pending)");
        }
        finally { if (tree.TenantId > 0) await WorkflowSupport.TeardownTree(tree.TenantId, tree.LeaseId, tree.PropertyId); }
    }

    [Fact]
    public async Task Refund_amount_exceeding_payment_is_rejected()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        WorkflowTree tree = default;
        try
        {
            tree = await WorkflowScenario.BuildToInvoiceAsync(page, ctx);
            var paymentId = await RecordAndApproveFullAsync(page, ctx, tree);

            // Attempt to refund more than was ever paid → controller redirects back, no row.
            var overAmount = tree.InvoiceAmount + 100m;
            (await WorkflowSupport.PostForm(ctx, "/Refunds/Create", new[]
            {
                ("paymentId", paymentId.ToString()),
                ("amount", overAmount.ToString("0.000")),
                ("method", "Cash"),
                ("reason", "too much"),
                ("referenceNumber", "REF1"),
            }, $"/Refunds/Create?paymentId={paymentId}")).Should().BeInRange(200, 399);

            (await WorkflowSupport.Scalar<int>("SELECT COUNT(*) FROM Refunds WHERE PaymentId=@p", ("@p", paymentId)))
                .Should().Be(0, "a refund exceeding the payment's AmountPaid must not create a Refund row");

            Console.WriteLine($"REFUND_OVER_REJECTED_OK payment={paymentId} amount={overAmount} → no row");
        }
        finally { if (tree.TenantId > 0) await WorkflowSupport.TeardownTree(tree.TenantId, tree.LeaseId, tree.PropertyId); }
    }

    [Fact]
    public async Task Settle_before_approve_is_blocked()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        WorkflowTree tree = default;
        try
        {
            tree = await WorkflowScenario.BuildToInvoiceAsync(page, ctx);
            var paymentId = await RecordAndApproveFullAsync(page, ctx, tree);

            // Create a Pending refund but do NOT approve it.
            var refundAmount = Math.Round(tree.InvoiceAmount / 2, 3);
            (await WorkflowSupport.PostForm(ctx, "/Refunds/Create", new[]
            {
                ("paymentId", paymentId.ToString()),
                ("amount", refundAmount.ToString("0.000")),
                ("method", "Cash"),
                ("reason", "pending"),
                ("referenceNumber", "REF1"),
            }, $"/Refunds/Create?paymentId={paymentId}")).Should().BeInRange(200, 399);

            var refundId = await LatestRefundIdAsync(paymentId);
            refundId.Should().BeGreaterThan(0, "the refund row must have been created");

            // Settle directly on a Pending refund → guarded, no state change.
            (await WorkflowSupport.PostForm(ctx, $"/Refunds/Settle/{refundId}")).Should().BeInRange(200, 399);

            (await WorkflowSupport.Scalar<string>("SELECT Status FROM Refunds WHERE RefundId=@r", ("@r", refundId)))
                .Should().Be("Pending", "only Approved refunds can be settled — Status must remain Pending");
            (await WorkflowSupport.Scalar<decimal>("SELECT PaidAmount FROM Invoices WHERE InvoiceId=@i", ("@i", tree.InvoiceId)))
                .Should().Be(tree.InvoiceAmount, "a blocked settle must leave the invoice PaidAmount untouched");

            Console.WriteLine($"SETTLE_GUARD_OK refund={refundId} stayed Pending, invoice PaidAmount={tree.InvoiceAmount} unchanged");
        }
        finally { if (tree.TenantId > 0) await WorkflowSupport.TeardownTree(tree.TenantId, tree.LeaseId, tree.PropertyId); }
    }
}
