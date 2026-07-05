using Microsoft.Playwright;
using RentCollection.E2ETests.Pages;

namespace RentCollection.E2ETests.Helpers;

/// <summary>Ids of a workflow tree provisioned by <see cref="WorkflowScenario"/>.</summary>
public readonly record struct WorkflowTree(
    int PropertyId, int UnitId, int TenantId, int LeaseId, int InvoiceId, decimal InvoiceAmount, string Suffix);

/// <summary>
/// Provisions the shared Property → Unit → Tenant → Lease(Active) → Invoice
/// tree through the real UI, so each payment-path test only has to add its own
/// payment step + assertions + teardown (<see cref="WorkflowSupport.TeardownTree"/>).
/// </summary>
public static class WorkflowScenario
{
    public const decimal Rent = 300.000m;

    public static async Task<WorkflowTree> BuildToInvoiceAsync(IPage page, IBrowserContext ctx)
    {
        var suffix = AdminSession.UniqueSuffix();
        var digits = Math.Abs(Guid.NewGuid().GetHashCode()).ToString().PadLeft(12, '0').Substring(0, 12);
        var firstOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

        // 1. Property
        var prop = new PropertyCreatePage(page);
        await prop.GoToAsync();
        var propName = $"WF Prop {suffix}";
        await prop.FillRequiredAsync(propName, $"WFP{digits}", numberOfUnits: 5, companyIdValue: "1");
        await prop.FillOptionalAsync();
        await prop.SubmitAsync();
        var propertyId = await WorkflowSupport.Scalar<int>(
            "SELECT ISNULL((SELECT TOP 1 PropertyId FROM Properties WHERE PropertyName=@n ORDER BY PropertyId DESC),0)", ("@n", propName));

        // 2. Unit
        var unitPage = new UnitCreatePage(page);
        await unitPage.GoToAsync(propertyId);
        await unitPage.FillAsync($"U{suffix}", Rent, propertyIdValue: propertyId.ToString());
        var unitId = await unitPage.SubmitAndGetUnitIdAsync();

        // 3. Tenant (non-matching Building PACI → no auto-lease)
        var tenant = new TenantCreatePage(page);
        await tenant.GoToAsync();
        var tLast = $"WFT{suffix}";
        await tenant.FirstName.FillAsync("Flow");
        await tenant.LastName.FillAsync(tLast);
        await tenant.Email.FillAsync($"flow+{suffix}@example.com");
        await tenant.Phone.FillAsync("99000999");
        await tenant.Paci.FillAsync(digits);
        await tenant.BuildingPaci.FillAsync("WF-NOMATCH");
        await tenant.FillOptionalAsync(digits);
        await tenant.SubmitAsync();
        var tenantId = await WorkflowSupport.Scalar<int>(
            "SELECT ISNULL((SELECT TOP 1 TenantId FROM Tenants WHERE LastName=@l ORDER BY TenantId DESC),0)", ("@l", tLast));

        // 4. Lease (deposit 0 → Active; unit → Occupied)
        var lease = new LeaseCreatePage(page);
        await lease.GoToAsync(unitId);
        await lease.FillAsync(tenantId.ToString(), firstOfMonth, Rent, securityDeposit: 0m);
        await lease.FillOptionalAsync();
        await lease.SubmitAsync();
        var leaseId = await WorkflowSupport.Scalar<int>(
            "SELECT ISNULL((SELECT TOP 1 LeaseId FROM Leases WHERE TenantId=@t AND UnitId=@u ORDER BY LeaseId DESC),0)", ("@t", tenantId), ("@u", unitId));

        // 5. Invoice (trigger monthly generation)
        await WorkflowSupport.PostForm(ctx, "/Invoices/GenerateMonthlyInvoices");
        var invoiceId = await WorkflowSupport.Scalar<int>(
            "SELECT ISNULL((SELECT TOP 1 InvoiceId FROM Invoices WHERE LeaseId=@l AND InvoiceType='Rent' ORDER BY InvoiceId DESC),0)", ("@l", leaseId));
        var invAmount = await WorkflowSupport.Scalar<decimal>("SELECT TotalAmount FROM Invoices WHERE InvoiceId=@i", ("@i", invoiceId));

        return new WorkflowTree(propertyId, unitId, tenantId, leaseId, invoiceId, invAmount, suffix);
    }
}
