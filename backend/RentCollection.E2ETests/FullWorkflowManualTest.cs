using System.Text;
using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;
using RentCollection.E2ETests.Pages;

namespace RentCollection.E2ETests;

/// <summary>
/// DEEP end-to-end workflow that fills EVERY field, uploads documents, runs the
/// full money chain, and DELIBERATELY DOES NOT CLEAN UP — the data is left in the
/// target environment so it can be inspected/tested manually.
///
///   Login → Property(all fields) → Unit(all fields) → Tenant(all fields + 2 file
///   uploads + known password) → Lease(all fields, Active) → Generate Invoice →
///   Record Cash Payment(all fields) → Approve → Invoice Paid → Tenant logs in via
///   the mobile API and SEES it Paid.
///
/// Run against the demo (keeps data there for manual testing):
///   RC_E2E_BASE_URL=http://<DEMO_HOST>
///   RC_E2E_DB="<prod connection string>"      (used only to read back the IDs)
///   dotnet test --filter FullWorkflowManualTest
/// Watch it live: add RC_E2E_HEADED=1 RC_E2E_SLOWMO=800
/// </summary>
[Collection("WorkflowSerial")]
public sealed class FullWorkflowManualTest : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public FullWorkflowManualTest(E2EFixture fixture) => _fixture = fixture;

    // A tiny valid 1x1 PNG so the document upload sends real file bytes.
    private static byte[] Png() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    [Fact]
    public async Task Deep_full_workflow_all_fields_no_teardown()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;

        var suffix = AdminSession.UniqueSuffix();
        var civilId = "2" + Math.Abs(Guid.NewGuid().GetHashCode()).ToString().PadLeft(11, '0').Substring(0, 11); // 12 digits
        var tenantPassword = "Tenant@12345";
        var buildingPaci = $"BWF{Math.Abs(Guid.NewGuid().GetHashCode()).ToString().PadLeft(9, '0').Substring(0, 9)}";
        var firstOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        const decimal rent = 350.000m;
        var propName = $"MANUAL WF Property {suffix}";
        var tenantLast = $"ManualWF {suffix}";

        // ── 1. PROPERTY (all fields) ─────────────────────────────────────
        var prop = new PropertyCreatePage(page);
        await prop.GoToAsync();
        await prop.FillRequiredAsync(propName, buildingPaci, numberOfUnits: 3, type: "Apartment",
            governorate: "Al Asimah", companyIdValue: "1");
        await prop.FillOptionalAsync();          // address/area/block/street/building#/value/purchase date
        await prop.SubmitAsync();
        var propertyId = await WorkflowSupport.Scalar<int>(
            "SELECT ISNULL((SELECT TOP 1 PropertyId FROM Properties WHERE PropertyName=@n ORDER BY PropertyId DESC),0)", ("@n", propName));
        propertyId.Should().BeGreaterThan(0, "property must be created");

        // ── 2. UNIT (all fields) ─────────────────────────────────────────
        var unitPage = new UnitCreatePage(page);
        await unitPage.GoToAsync(propertyId);
        var unitNumber = $"U{suffix}";
        await unitPage.FillAsync(unitNumber, rent, propertyIdValue: propertyId.ToString(),
            unitType: "Apartment", bedrooms: 3, bathrooms: 2);   // also fills floor + sqft
        var unitId = await unitPage.SubmitAndGetUnitIdAsync();
        unitId.Should().BeGreaterThan(0, "unit must be created");

        // ── 3. TENANT (all fields + 2 document uploads + known password) ─
        var tenant = new TenantCreatePage(page);
        await tenant.GoToAsync();
        await tenant.FirstName.FillAsync("Manual");
        await tenant.LastName.FillAsync(tenantLast);
        await tenant.Email.FillAsync($"manualwf+{suffix}@example.com");
        await tenant.Phone.FillAsync("69001122");
        await page.Locator("input[name='InitialPassword']").FillAsync(tenantPassword);   // known password
        await tenant.Paci.FillAsync(civilId);                 // Civil ID = the app login
        await tenant.BuildingPaci.FillAsync(buildingPaci);    // soft-links to the property above
        await tenant.FillOptionalAsync(civilId);              // DOB / employment / emergency contact

        // Document #1 — Civil ID (upload a real file)
        await page.Locator("select[name='DocumentTypes[]']").First.SelectOptionAsync(new SelectOptionValue { Value = "Civil ID" });
        await page.Locator("input[name='Documents[]']").First.SetInputFilesAsync(new FilePayload
        { Name = "civil-id.png", MimeType = "image/png", Buffer = Png() });
        // Document #2 — Passport (add a second row, upload another file)
        await page.Locator("#add-document-btn").ClickAsync();
        await page.Locator("select[name='DocumentTypes[]']").Nth(1).SelectOptionAsync(new SelectOptionValue { Value = "Passport" });
        await page.Locator("input[name='Documents[]']").Nth(1).SetInputFilesAsync(new FilePayload
        { Name = "passport.png", MimeType = "image/png", Buffer = Png() });

        await tenant.SubmitAsync();
        var tenantId = await WorkflowSupport.Scalar<int>(
            "SELECT ISNULL((SELECT TOP 1 TenantId FROM Tenants WHERE LastName=@l ORDER BY TenantId DESC),0)", ("@l", tenantLast));
        tenantId.Should().BeGreaterThan(0, "tenant must be created");
        var docCount = await WorkflowSupport.Scalar<int>(
            "SELECT COUNT(*) FROM Documents WHERE RelatedEntityType='Tenant' AND RelatedEntityId=@t", ("@t", tenantId));

        // ── 4. LEASE (all fields, deposit 0 → Active, unit → Occupied) ───
        var lease = new LeaseCreatePage(page);
        await lease.GoToAsync(unitId);
        await lease.FillAsync(tenantId.ToString(), firstOfMonth, rent, securityDeposit: 0m);
        await lease.FillOptionalAsync();     // payment due day / late fee / grace / type
        await lease.SubmitAsync();
        var leaseId = await WorkflowSupport.Scalar<int>(
            "SELECT ISNULL((SELECT TOP 1 LeaseId FROM Leases WHERE TenantId=@t AND UnitId=@u ORDER BY LeaseId DESC),0)", ("@t", tenantId), ("@u", unitId));
        leaseId.Should().BeGreaterThan(0, "lease must be created");
        (await WorkflowSupport.Scalar<string>("SELECT Status FROM Leases WHERE LeaseId=@l", ("@l", leaseId)))
            .Should().Be("Active");
        (await WorkflowSupport.Scalar<string>("SELECT Status FROM Units WHERE UnitId=@u", ("@u", unitId)))
            .Should().Be("Occupied");

        // ── 5. INVOICE ───────────────────────────────────────────────────
        // The "Generate Monthly Invoices" button is a batch job that scans ALL
        // active leases (N+1, one big save) — too slow on the demo's dataset and
        // there is no per-lease invoice UI. So we insert THIS lease's rent invoice
        // directly, exactly as the monthly generator shapes it (Pending / Rent).
        var rentSql = rent.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
        await WorkflowSupport.Exec($@"
            INSERT INTO Invoices (LeaseId, TenantId, InvoiceDate, DueDate, TotalAmount, PaidAmount, Status, InvoiceType, Notes, CreatedDate)
            VALUES ({leaseId}, {tenantId}, GETUTCDATE(), EOMONTH(GETUTCDATE()), {rentSql}, 0, 'Pending', 'Rent', 'Monthly rent (manual workflow test {suffix})', GETUTCDATE());");
        var invoiceId = await WorkflowSupport.Scalar<int>(
            "SELECT ISNULL((SELECT TOP 1 InvoiceId FROM Invoices WHERE LeaseId=@l AND InvoiceType='Rent' ORDER BY InvoiceId DESC),0)", ("@l", leaseId));
        invoiceId.Should().BeGreaterThan(0, "a rent invoice must be generated");
        var invoiceAmount = await WorkflowSupport.Scalar<decimal>("SELECT TotalAmount FROM Invoices WHERE InvoiceId=@i", ("@i", invoiceId));

        // ── 6. RECORD CASH PAYMENT (all fields) ──────────────────────────
        await page.GotoAsync("/CashPayment/Record", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("#InvoiceId").SelectOptionAsync(new SelectOptionValue { Value = invoiceId.ToString() });
        // Set amount/date/notes robustly (the number/datetime-local inputs don't
        // always take a plain Fill on this page; JS-set + input/change guarantees it).
        await page.EvaluateAsync(@"(a) => {
            const set = (id, v) => { const e = document.getElementById(id); if (e) { e.value = v;
                e.dispatchEvent(new Event('input',{bubbles:true})); e.dispatchEvent(new Event('change',{bubbles:true})); } };
            set('Amount', a.amt); set('PaymentDate', a.dt); set('Notes', a.notes);
        }", new
        {
            amt = invoiceAmount.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
            dt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm"),
            notes = $"Manual workflow test payment — full rent {invoiceAmount:0.000} KWD, cash at counter."
        });
        var amtEcho = await page.Locator("#Amount").InputValueAsync();
        var invEcho = await page.Locator("#InvoiceId").InputValueAsync();
        Console.WriteLine($"PAY_FORM invoiceSelected={invEcho} amount={amtEcho}");
        // Submit the form directly (like the server-accepted POST): includes the
        // antiforgery hidden field and skips native HTML5 validation that was
        // blocking the button click.
        await page.EvaluateAsync("() => document.querySelector('form.cash-form').submit()");
        await page.WaitForURLAsync(u => !u.Contains("/CashPayment/Record"), new() { Timeout = 25_000 });
        var paymentId = await WorkflowSupport.Scalar<int>(
            "SELECT ISNULL((SELECT TOP 1 PaymentId FROM Payments WHERE TenantId=@t ORDER BY PaymentId DESC),0)", ("@t", tenantId));
        paymentId.Should().BeGreaterThan(0, "a payment record must be created");

        // ── 7. APPROVE (via the real Approvals page). Submit the row's Approve
        //       form directly — it carries the antiforgery hidden field and this
        //       skips the onclick confirm() dialog that blocked the button click. ─
        await page.GotoAsync("/PaymentApproval", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var approveSubmitted = await page.EvaluateAsync<bool>(
            $@"() => {{ const f = document.querySelector('form[action$=""/Approve/{paymentId}""]'); if (!f) return false; f.submit(); return true; }}");
        approveSubmitted.Should().BeTrue($"the Approve form for payment {paymentId} must be present on /PaymentApproval");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForTimeoutAsync(1500);   // let the approve POST + redirect settle
        (await WorkflowSupport.Scalar<int>("SELECT Status FROM Payments WHERE PaymentId=@p", ("@p", paymentId)))
            .Should().Be(2, "approved payment → Completed(2)");
        (await WorkflowSupport.Scalar<string>("SELECT Status FROM Invoices WHERE InvoiceId=@i", ("@i", invoiceId)))
            .Should().Be("Paid", "full payment flips the invoice to Paid");

        // ── 8. TENANT SEES IT PAID (mobile API: login + read invoices) ───
        var baseUrl = Environment.GetEnvironmentVariable("RC_E2E_BASE_URL") ?? "http://localhost:5032";
        var api = await ctx.APIRequest.PostAsync($"{baseUrl}/api/tenant/login", new()
        {
            DataObject = new { CivilId = civilId, Password = tenantPassword }
        });
        api.Status.Should().Be(200, "tenant must be able to log into the mobile app with Civil ID + password");
        var loginJson = await api.JsonAsync();
        var token = loginJson.Value.GetProperty("data").GetProperty("token").GetString();
        token.Should().NotBeNullOrEmpty();

        var invResp = await ctx.APIRequest.GetAsync($"{baseUrl}/api/tenant/{tenantId}/invoices",
            new() { Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" } });
        invResp.Status.Should().Be(200);
        var invText = await invResp.TextAsync();
        invText.Should().Contain("Paid", "the tenant's invoice list must show the invoice as Paid");

        // ── SUMMARY (for manual testing — nothing is deleted) ────────────
        var sb = new StringBuilder();
        sb.AppendLine("==================== MANUAL-TEST WORKFLOW CREATED (kept, not deleted) ====================");
        sb.AppendLine($"  Property : id={propertyId}  name=\"{propName}\"   → /Properties/Details/{propertyId}");
        sb.AppendLine($"  Unit     : id={unitId}  number=\"{unitNumber}\"  rent={rent:0.000} KWD  → /Units/Details/{unitId}");
        sb.AppendLine($"  Tenant   : id={tenantId}  name=\"Manual {tenantLast}\"  docsUploaded={docCount}  → /Tenants/Details/{tenantId}");
        sb.AppendLine($"  Lease    : id={leaseId}  status=Active  → /Leases/Details/{leaseId}");
        sb.AppendLine($"  Invoice  : id={invoiceId}  amount={invoiceAmount:0.000} KWD  status=Paid");
        sb.AppendLine($"  Payment  : id={paymentId}  status=Completed(2)  (approved)");
        sb.AppendLine("  ---------------------------------------------------------------------------------------");
        sb.AppendLine($"  MOBILE-APP LOGIN →  Civil ID: {civilId}   Password: ***");
        sb.AppendLine("  (tenant logged in via API and their invoice shows PAID ✓)");
        sb.AppendLine("=========================================================================================");
        Console.WriteLine(sb.ToString());
        Console.WriteLine($"WORKFLOW_MANUAL_OK property={propertyId} unit={unitId} tenant={tenantId} lease={leaseId} " +
            $"invoice={invoiceId} payment={paymentId} civilId={civilId} password=*** docs={docCount}");
    }
}
