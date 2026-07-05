using System.Text.Json;
using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// Verifies the "oldest-unpaid-first" payment eligibility guard: with two
/// unpaid invoices for one tenant, paying the NEWER one is blocked (409),
/// while paying the OLDER one is allowed. Exercised via the KNET initiate
/// endpoint, whose eligibility check returns Conflict when an older invoice
/// is still unpaid.
/// </summary>
[Collection("WorkflowSerial")]
public sealed class PaymentEligibilityGuardTest : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public PaymentEligibilityGuardTest(E2EFixture fixture) => _fixture = fixture;

    private static Task<IAPIResponse> KnetInitiate(IBrowserContext ctx, WorkflowTree tree, int invoiceId, decimal amount) =>
        ctx.APIRequest.PostAsync("/api/payments/knet/initiate", new APIRequestContextOptions
        {
            Headers = new Dictionary<string, string> { ["content-type"] = "application/json" },
            Data = JsonSerializer.Serialize(new
            {
                invoiceId,
                tenantId = tree.TenantId,
                propertyId = tree.PropertyId,
                leaseId = tree.LeaseId,
                amount,
            }),
        });

    [Fact]
    public async Task Cannot_pay_newer_invoice_while_an_older_one_is_unpaid()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;

        WorkflowTree tree = default;
        try
        {
            // B = the newer invoice from the standard tree.
            tree = await WorkflowScenario.BuildToInvoiceAsync(page, ctx);
            var newer = tree.InvoiceId;

            // A = an OLDER unpaid rent invoice for the same lease/tenant.
            await WorkflowSupport.Exec($@"
                INSERT INTO Invoices (LeaseId, TenantId, InvoiceDate, DueDate, TotalAmount, PaidAmount, Status, InvoiceType, CreatedDate)
                VALUES ({tree.LeaseId}, {tree.TenantId},
                        CAST(DATEADD(month,-2,GETUTCDATE()) AS date),
                        CAST(DATEADD(month,-2,GETUTCDATE()) AS date),
                        {WorkflowScenario.Rent.ToString("0.000")}, 0, 'Pending', 'Rent', GETUTCDATE());");
            var older = await WorkflowSupport.Scalar<int>(
                "SELECT TOP 1 InvoiceId FROM Invoices WHERE LeaseId=@l ORDER BY InvoiceDate ASC, InvoiceId ASC", ("@l", tree.LeaseId));
            older.Should().BeGreaterThan(0).And.NotBe(newer, "there must be a distinct older invoice");

            // Paying the NEWER invoice must be blocked while the older is unpaid.
            var blocked = await KnetInitiate(ctx, tree, newer, tree.InvoiceAmount);
            blocked.Status.Should().Be(409, "the guard must block paying a newer invoice before the oldest is settled");

            // Paying the OLDER (oldest) invoice is allowed.
            var allowed = await KnetInitiate(ctx, tree, older, WorkflowScenario.Rent);
            allowed.Status.Should().BeInRange(200, 299, "the oldest unpaid invoice is payable");

            Console.WriteLine($"ELIGIBILITY_OK older={older}(allowed) newer={newer}(blocked-409)");
        }
        finally
        {
            if (tree.TenantId > 0)
                await WorkflowSupport.TeardownTree(tree.TenantId, tree.LeaseId, tree.PropertyId);
        }
    }
}
