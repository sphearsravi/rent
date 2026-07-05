using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// Extra payment/approval workflow scenarios beyond the happy path:
///   • partial payment → then full (invoice Partially Paid → Paid)
///   • paying an Overdue invoice (still settles to Paid)
///   • approving through the /PaymentApproval Details UI (not just the endpoint)
/// All reuse WorkflowScenario, verify against the DB, and self-clean.
/// </summary>
[Collection("WorkflowSerial")]
public sealed class PaymentApprovalScenariosTests : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public PaymentApprovalScenariosTests(E2EFixture fixture) => _fixture = fixture;

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

    private static async Task<string?> PollInvoice(int invoiceId, string target, int tries = 15)
    {
        string? last = null;
        for (int i = 0; i < tries; i++)
        {
            last = await WorkflowSupport.Scalar<string>("SELECT Status FROM Invoices WHERE InvoiceId=@i", ("@i", invoiceId));
            if (last == target) return last;
            await Task.Delay(700);
        }
        return last;
    }

    [Fact]
    public async Task Partial_payment_then_full_moves_invoice_PartiallyPaid_then_Paid()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        WorkflowTree tree = default;
        try
        {
            tree = await WorkflowScenario.BuildToInvoiceAsync(page, ctx);
            var half = Math.Round(tree.InvoiceAmount / 2, 3);

            // First half → approve → Partially Paid
            var p1 = await RecordCashAsync(page, tree, half);
            (await WorkflowSupport.PostForm(ctx, $"/PaymentApproval/Approve/{p1}")).Should().BeInRange(200, 399);
            (await WorkflowSupport.Scalar<string>("SELECT Status FROM Invoices WHERE InvoiceId=@i", ("@i", tree.InvoiceId)))
                .Should().Be("Partially Paid");
            (await WorkflowSupport.Scalar<decimal>("SELECT PaidAmount FROM Invoices WHERE InvoiceId=@i", ("@i", tree.InvoiceId)))
                .Should().Be(half);

            // Remaining balance → approve → Paid
            var p2 = await RecordCashAsync(page, tree, tree.InvoiceAmount - half);
            (await WorkflowSupport.PostForm(ctx, $"/PaymentApproval/Approve/{p2}")).Should().BeInRange(200, 399);
            (await WorkflowSupport.Scalar<string>("SELECT Status FROM Invoices WHERE InvoiceId=@i", ("@i", tree.InvoiceId)))
                .Should().Be("Paid");

            Console.WriteLine($"PARTIAL_OK invoice={tree.InvoiceId} {half}→PartiallyPaid, full→Paid");
        }
        finally { if (tree.TenantId > 0) await WorkflowSupport.TeardownTree(tree.TenantId, tree.LeaseId, tree.PropertyId); }
    }

    [Fact]
    public async Task Overdue_invoice_can_be_paid_to_Paid()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        WorkflowTree tree = default;
        try
        {
            tree = await WorkflowScenario.BuildToInvoiceAsync(page, ctx);
            await WorkflowSupport.Exec(
                $"UPDATE Invoices SET Status='Overdue', DueDate=DATEADD(day,-10,GETUTCDATE()) WHERE InvoiceId={tree.InvoiceId}");
            (await WorkflowSupport.Scalar<string>("SELECT Status FROM Invoices WHERE InvoiceId=@i", ("@i", tree.InvoiceId)))
                .Should().Be("Overdue");

            var p = await RecordCashAsync(page, tree, tree.InvoiceAmount);
            (await WorkflowSupport.PostForm(ctx, $"/PaymentApproval/Approve/{p}")).Should().BeInRange(200, 399);
            (await WorkflowSupport.Scalar<string>("SELECT Status FROM Invoices WHERE InvoiceId=@i", ("@i", tree.InvoiceId)))
                .Should().Be("Paid", "an overdue invoice paid in full must settle to Paid");

            Console.WriteLine($"OVERDUE_PAY_OK invoice={tree.InvoiceId} Overdue→Paid");
        }
        finally { if (tree.TenantId > 0) await WorkflowSupport.TeardownTree(tree.TenantId, tree.LeaseId, tree.PropertyId); }
    }

    [Fact]
    public async Task Approve_through_the_PaymentApproval_Details_UI()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        WorkflowTree tree = default;
        try
        {
            tree = await WorkflowScenario.BuildToInvoiceAsync(page, ctx);
            var p = await RecordCashAsync(page, tree, tree.InvoiceAmount);

            // Drive the real approval UI: Details → Approve (modal) → Confirm.
            await page.GotoAsync($"/PaymentApproval/Details/{p}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await page.Locator(".btn-approve").First.ClickAsync();                 // opens the confirm modal
            await page.Locator(".btn-confirm-approve").First.ClickAsync(new() { NoWaitAfter = true });

            var status = await PollInvoice(tree.InvoiceId, "Paid");
            status.Should().Be("Paid", "approving via the Details UI must mark the invoice Paid");
            (await WorkflowSupport.Scalar<int>("SELECT Status FROM Payments WHERE PaymentId=@p", ("@p", p)))
                .Should().Be(2, "the payment must be Completed(2) after UI approval");

            Console.WriteLine($"UI_APPROVE_OK payment={p} invoice={tree.InvoiceId} → Paid via Details UI");
        }
        finally { if (tree.TenantId > 0) await WorkflowSupport.TeardownTree(tree.TenantId, tree.LeaseId, tree.PropertyId); }
    }
}
