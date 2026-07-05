using FluentAssertions;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// Verifies the effects of the Hangfire recurring jobs by triggering them
/// on-demand (POST /Reports/TriggerJob) and polling the DB for the outcome.
/// Each test forces a known pre-state on a freshly-provisioned tree so the
/// assertion is deterministic regardless of the wall-clock date, then tears down.
/// </summary>
[Collection("WorkflowSerial")]
public sealed class BackgroundJobEffectTests : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public BackgroundJobEffectTests(E2EFixture fixture) => _fixture = fixture;

    /// <summary>Poll a scalar until it equals <paramref name="target"/> (jobs run async).</summary>
    private static async Task<string?> PollUntil(string sql, (string, object) p, string target, int tries = 25)
    {
        string? last = null;
        for (int i = 0; i < tries; i++)
        {
            last = await WorkflowSupport.Scalar<string>(sql, p);
            if (last == target) return last;
            await Task.Delay(1000);
        }
        return last;
    }

    [Fact]
    public async Task FlipOverdueInvoices_marks_a_pending_past_due_invoice_overdue()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        WorkflowTree tree = default;
        try
        {
            tree = await WorkflowScenario.BuildToInvoiceAsync(page, ctx);

            // Force the invoice to be Pending + past-due (deterministic pre-state).
            await WorkflowSupport.Exec(
                $"UPDATE Invoices SET Status='Pending', DueDate=DATEADD(day,-30,GETUTCDATE()) WHERE InvoiceId={tree.InvoiceId}");
            (await WorkflowSupport.Scalar<string>("SELECT Status FROM Invoices WHERE InvoiceId=@i", ("@i", tree.InvoiceId)))
                .Should().Be("Pending");

            // Trigger the job (enqueues to Hangfire) and wait for the effect.
            (await WorkflowSupport.PostForm(ctx, "/Reports/TriggerJob",
                new[] { ("jobId", "flip-overdue-invoices") })).Should().BeInRange(200, 299);

            var status = await PollUntil("SELECT Status FROM Invoices WHERE InvoiceId=@i", ("@i", tree.InvoiceId), "Overdue");
            status.Should().Be("Overdue", "the FlipOverdueInvoices job must flip a pending past-due invoice to Overdue");

            Console.WriteLine($"JOB_OVERDUE_OK invoice={tree.InvoiceId} → Overdue");
        }
        finally
        {
            if (tree.TenantId > 0) await WorkflowSupport.TeardownTree(tree.TenantId, tree.LeaseId, tree.PropertyId);
        }
    }

    [Fact]
    public async Task ReconcileUnitStatus_reoccupies_a_leased_unit_wrongly_marked_vacant()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        WorkflowTree tree = default;
        try
        {
            tree = await WorkflowScenario.BuildToInvoiceAsync(page, ctx);

            // The active lease made the unit Occupied — introduce a discrepancy.
            await WorkflowSupport.Exec($"UPDATE Units SET Status='Vacant' WHERE UnitId={tree.UnitId}");
            (await WorkflowSupport.Scalar<string>("SELECT Status FROM Units WHERE UnitId=@u", ("@u", tree.UnitId)))
                .Should().Be("Vacant");

            (await WorkflowSupport.PostForm(ctx, "/Reports/TriggerJob",
                new[] { ("jobId", "reconcile-unit-status") })).Should().BeInRange(200, 299);

            var status = await PollUntil("SELECT Status FROM Units WHERE UnitId=@u", ("@u", tree.UnitId), "Occupied");
            status.Should().Be("Occupied", "ReconcileUnitStatus must re-occupy a unit that has an active lease");

            Console.WriteLine($"JOB_RECONCILE_OK unit={tree.UnitId} → Occupied");
        }
        finally
        {
            if (tree.TenantId > 0) await WorkflowSupport.TeardownTree(tree.TenantId, tree.LeaseId, tree.PropertyId);
        }
    }
}
