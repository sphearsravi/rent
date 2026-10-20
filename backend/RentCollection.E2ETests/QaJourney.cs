using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace RentCollection.E2ETests;

/// <summary>
/// Exploratory "professional tester" journey against a LIVE admin site:
/// property → units → tenant → lease (deposit → Activate) → duplicate lease
/// → terminate → re-lease → delete with dependents, plus the negative cases
/// a tester tries on every form. Nothing here hard-fails; every step records
/// PASS / FAIL / NOTE into a markdown report so the run always completes and
/// the findings are reviewable with screenshots.
///
/// Opt-in: RC_QA_BASE_URL, RC_QA_EMAIL, RC_QA_PASS. Output: C:\Temp\qa\
/// (RC_QA_OUT). Creates records named "QA-&lt;tag&gt;" on the target site.
/// </summary>
[Trait("Category", "QaJourney")]
public sealed class QaJourney : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fx;
    public QaJourney(E2EFixture fx) => _fx = fx;

    private readonly StringBuilder _report = new();
    private bool _buildingPaciNoted;
    private string _propertyPaci = "";
    private string _out = @"C:\Temp\qa";
    private int _shot;
    private IPage _page = null!;

    private void Log(string verdict, string step, string detail = "")
        => _report.AppendLine($"| {verdict} | {step} | {detail.Replace("|", "/").Replace("\n", " ")} |");

    private async Task<string> Shot(string name)
    {
        var file = Path.Combine(_out, $"{++_shot:00}-{name}.png");
        await _page.ScreenshotAsync(new PageScreenshotOptions { Path = file, FullPage = true });
        return Path.GetFileName(file);
    }

    private async Task<string> Alerts()
    {
        var parts = new List<string>();
        foreach (var sel in new[] { ".alert-success", ".alert-danger", ".alert-warning", ".alert-info", ".validation-summary-errors", ".lw-conflict" })
            foreach (var el in await _page.QuerySelectorAllAsync(sel))
            {
                var t = (await el.InnerTextAsync()).Trim();
                if (t.Length > 0) parts.Add($"[{sel.TrimStart('.')}] {Regex.Replace(t, @"\s+", " ")}");
            }
        return string.Join(" ‖ ", parts);
    }

    private async Task<int> FieldErrors()
    {
        var n = 0;
        foreach (var sel in new[] { ".field-validation-error", ".fr-hint.is-error", ".text-danger", ".invalid-feedback" })
            foreach (var el in await _page.QuerySelectorAllAsync(sel))
                if (!string.IsNullOrWhiteSpace(await el.InnerTextAsync())) n++;
        return n;
    }

    private async Task SelectFirstReal(string selector)
    {
        var opts = await _page.Locator($"{selector} option").AllAsync();
        foreach (var o in opts)
        {
            var v = await o.GetAttributeAsync("value");
            if (!string.IsNullOrEmpty(v)) { await _page.SelectOptionAsync(selector, v); return; }
        }
    }

    /// <summary>Open every collapsed "More details" zone so optional fields are fillable.</summary>
    private async Task Expand()
    {
        foreach (var b in await _page.QuerySelectorAllAsync("button[data-fr-toggle]"))
        {
            var target = await b.GetAttributeAsync("data-fr-toggle");
            var panel = await _page.QuerySelectorAsync($"[data-fr-collapsible='{target}']");
            if (panel != null && !await panel.IsVisibleAsync()) await b.ClickAsync();
        }
        await _page.WaitForTimeoutAsync(200);
    }

    private async Task SubmitForm(string formSelector = "form.form-revamp, form[method='post']")
    {
        var btn = _page.Locator($"{formSelector} button[type='submit']:not([data-fr-save-and-add])").Last;
        await btn.ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [Fact]
    public async Task Property_units_tenant_lease_journey()
    {
        var baseUrl = Environment.GetEnvironmentVariable("RC_QA_BASE_URL");
        var email = Environment.GetEnvironmentVariable("RC_QA_EMAIL");
        var pass = Environment.GetEnvironmentVariable("RC_QA_PASS");
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(pass)) return;
        _out = Environment.GetEnvironmentVariable("RC_QA_OUT") ?? _out;
        Directory.CreateDirectory(_out);

        var tag = DateTime.Now.ToString("ddHHmm");
        var propName = $"QA Tower {tag}";
        var tenantFirst = "QA"; var tenantLast = $"Tester {tag}";
        var civilId = "290010" + tag; // 12 digits (tag is ddHHmm)
        var serverErrors = new List<string>();

        await using var ctx = await _fx.Browser.NewContextAsync(new BrowserNewContextOptions
        { BaseURL = baseUrl, ViewportSize = new ViewportSize { Width = 1440, Height = 900 }, Locale = "en-US" });
        _page = await ctx.NewPageAsync();
        _page.Response += (_, r) => { if (r.Status >= 500) serverErrors.Add($"{r.Status} {r.Url}"); };
        // Accept every confirm() the app raises (Activate / Terminate / Delete all ask).
        _page.Dialog += async (_, d) => await d.AcceptAsync();

        _report.AppendLine($"# QA journey — {baseUrl} — {DateTime.Now:yyyy-MM-dd HH:mm}\n");
        _report.AppendLine("| Verdict | Step | Observed |\n|---|---|---|");

        // ── Login ──
        await _page.GotoAsync("/Account/Login");
        await _page.FillAsync("input[name='Email']", email);
        await _page.FillAsync("input[name='Password']", pass);
        await _page.Locator("form:has(input[name='Email']) button[type='submit']").First.ClickAsync();
        await _page.WaitForURLAsync(u => !u.Contains("/Account/Login"), new() { Timeout = 30000 });
        Log("PASS", "Login as admin", _page.Url);

        // ── 1. Property: empty submit ──
        await _page.GotoAsync("/Properties/Create");
        await _page.EvaluateAsync("document.querySelectorAll('form [required]').forEach(e => e.removeAttribute('required'))");
        await SubmitForm();
        var errs = await FieldErrors();
        Log(errs > 0 || _page.Url.Contains("/Create") ? "PASS" : "FAIL", "Property: empty form is rejected server-side",
            $"{errs} field errors; url={_page.Url}; {await Alerts()} ({await Shot("property-empty")})");

        // ── 2. Property: create ──
        await _page.GotoAsync("/Properties/Create");
        var companySel = _page.Locator("#CompanyId");
        var companyOpts = await companySel.CountAsync() > 0 ? await _page.Locator("#CompanyId option[value]:not([value=''])").CountAsync() : -1;
        Log("NOTE", "Property form asks a company-scoped Admin to pick a Company",
            companyOpts < 0 ? "no Company field (good)" : $"Company dropdown shown with {companyOpts} option(s) and nothing preselected");
        await _page.FillAsync("#PropertyName", propName);
        _propertyPaci = tag;
        await _page.FillAsync("#PACINumber", "9988" + tag);
        await SelectFirstReal("#PropertyType");
        await SelectFirstReal("#Governorate");
        if (companyOpts > 0) await SelectFirstReal("#CompanyId");
        await _page.FillAsync("#NumberOfUnits", "2");
        if (await _page.Locator("input[name='unitPrefix']").CountAsync() > 0) await _page.FillAsync("input[name='unitPrefix']", "QA-");
        await Expand();
        await _page.FillAsync("#Address", "QA Street 5, Block 9");
        await _page.FillAsync("#Area", "Salmiya");
        await _page.FillAsync("#Block", "9");
        await _page.FillAsync("#Street", "5");
        await _page.FillAsync("#BuildingNumber", "12");
        await SubmitForm();
        var propAlerts = await Alerts();
        Log(_page.Url.Contains("/Create") ? "FAIL" : "PASS", "Property: create", $"url={_page.Url}; {propAlerts} ({await Shot("property-created")})");

        // find the property id
        await _page.GotoAsync("/Properties");
        var card = _page.Locator($".lp-card[data-name='{propName}']");
        var propertyId = await card.CountAsync() > 0 ? await card.First.GetAttributeAsync("data-property-id") : null;
        Log(propertyId != null ? "PASS" : "FAIL", "Property appears in the list", $"id={propertyId} ({await Shot("properties-list")})");
        if (propertyId == null) { await Finish(serverErrors); return; }

        // Auto-created units?
        await _page.GotoAsync($"/Units?propertyId={propertyId}");
        var autoUnits = await _page.Locator("tr[data-search]").CountAsync();
        Log("NOTE", "Units auto-created from NumberOfUnits=2 + prefix", $"{autoUnits} unit row(s) exist before adding any ({await Shot("units-auto")})");

        // ── 3. Units: add two, then a duplicate number, then negative rent ──
        async Task<bool> AddUnit(string number, string rent)
        {
            await _page.GotoAsync($"/Units/Create?propertyId={propertyId}");
            await Expand();
            if (string.IsNullOrEmpty(await _page.Locator("#PropertyId").InputValueAsync())) await _page.SelectOptionAsync("#PropertyId", propertyId!);
            await _page.FillAsync("#UnitNumber", number);
            await SelectFirstReal("#UnitType");
            await _page.FillAsync("#MonthlyRent", rent);
            await SubmitForm();
            return !_page.Url.Contains("/Create");
        }
        var u1 = await AddUnit("QA-101", "350");
        Log(u1 ? "PASS" : "FAIL", "Unit QA-101 created (rent 350)", $"{await Alerts()} url={_page.Url}");
        var u2 = await AddUnit("QA-102", "400");
        Log(u2 ? "PASS" : "FAIL", "Unit QA-102 created (rent 400)", $"{await Alerts()}");
        var dup = await AddUnit("QA-101", "350");
        Log(dup ? "FAIL" : "PASS", "Duplicate unit number in same property is rejected",
            dup ? "a second QA-101 was accepted" : $"{await Alerts()} / {await FieldErrors()} field errors ({await Shot("unit-duplicate")})");
        await _page.GotoAsync($"/Units/Create?propertyId={propertyId}");
        await _page.FillAsync("#UnitNumber", "QA-103");
        await SelectFirstReal("#UnitType");
        await _page.EvaluateAsync("document.querySelector('#MonthlyRent').removeAttribute('min')");
        await _page.FillAsync("#MonthlyRent", "-5");
        await SubmitForm();
        Log(_page.Url.Contains("/Create") ? "PASS" : "FAIL", "Negative rent is rejected", $"{await Alerts()} / {await FieldErrors()} field errors; url={_page.Url}");

        await _page.GotoAsync($"/Units?propertyId={propertyId}");
        var unitRows = await _page.Locator("tr[data-search]").CountAsync();
        Log("NOTE", "Units list for the property", $"{unitRows} row(s) ({await Shot("units-list")})");
        var unitLink = _page.Locator("tr[data-search*='qa-101'] a[href^='/Units/Details/']").First;
        var unitHref = await unitLink.CountAsync() > 0 ? await unitLink.GetAttributeAsync("href") : null;
        var unitId = unitHref?.Split('/').Last();

        // ── 4. Tenant: bad Civil ID, duplicate Civil ID, then create ──
        async Task FillTenant(string cid, string first, string last, string mail)
        {
            await _page.GotoAsync("/Tenants/Create");
            await Expand();
            await _page.FillAsync("#FirstName", first);
            await _page.FillAsync("#LastName", last);
            await _page.FillAsync("#civilIdInput", cid);
            await _page.FillAsync("#Email", mail);
            await _page.FillAsync("#Phone", "99887766");
            var bp = _page.Locator("#BuildingPACINumber");
            if (await bp.CountAsync() > 0)
            {
                var req = await bp.GetAttributeAsync("required");
                if (req != null && !_buildingPaciNoted) { _buildingPaciNoted = true; Log("NOTE", "Tenant form: 'Building PACI' is a required field at tenant creation", "a tenant cannot be saved without a building PACI even though the lease is what links tenant → unit; the browser blocks submit with no server message"); }
                await bp.FillAsync("9988" + _propertyPaci); // the QA property's PACI
            }
            var dob = _page.Locator("#DateOfBirth");
            if (await dob.CountAsync() > 0 && await dob.IsVisibleAsync()) await dob.FillAsync("1990-01-01");
        }
        await FillTenant("12345", tenantFirst, tenantLast, $"qa{tag}@example.com");
        await _page.EvaluateAsync("document.querySelectorAll('form [pattern],[maxlength]').forEach(e => { e.removeAttribute('pattern'); e.removeAttribute('maxlength'); })");
        await SubmitForm();
        Log(_page.Url.Contains("/Create") ? "PASS" : "FAIL", "Tenant: 5-digit Civil ID is rejected", $"{await Alerts()} / {await FieldErrors()} field errors ({await Shot("tenant-bad-civilid")})");

        await FillTenant("290010112345", "Dup", "Demo", $"dup{tag}@example.com"); // demo tenant's Civil ID
        await SubmitForm();
        Log(_page.Url.Contains("/Create") ? "PASS" : "FAIL", "Tenant: duplicate Civil ID (demo tenant's) is rejected",
            $"url={_page.Url}; {await Alerts()} / {await FieldErrors()} field errors ({await Shot("tenant-dup-civilid")})");

        await FillTenant(civilId, tenantFirst, tenantLast, $"qa{tag}@example.com");
        await SubmitForm();
        Log(_page.Url.Contains("/Create") ? "FAIL" : "PASS", "Tenant: create", $"url={_page.Url}; {await Alerts()} ({await Shot("tenant-created")})");
        await _page.GotoAsync("/Tenants");
        var trow = _page.Locator($"tr[data-search*='{civilId}']");
        var tenantId = await trow.CountAsync() > 0 ? await trow.First.GetAttributeAsync("data-tenant-id") : null;
        Log(tenantId != null ? "PASS" : "FAIL", "Tenant appears in the list", $"id={tenantId}");
        if (tenantId == null || unitId == null) { await Finish(serverErrors); return; }

        // ── 5. Lease: end before start ──
        var today = DateTime.Today;
        async Task FillLease(string unit, string tenant, DateTime start, DateTime end, string rent, string deposit)
        {
            await _page.GotoAsync($"/Leases/Create?unitId={unit}");
            await _page.WaitForTimeoutAsync(400);
            await _page.SelectOptionAsync("#unitSelect", unit);
            await _page.SelectOptionAsync("#tenantSelect", tenant);
            await _page.FillAsync("#LeaseStartDate", start.ToString("yyyy-MM-dd"));
            await _page.FillAsync("#LeaseEndDate", end.ToString("yyyy-MM-dd"));
            await _page.FillAsync("#MonthlyRent", rent);
            await _page.FillAsync("#SecurityDeposit", deposit);
            await _page.EvaluateAsync("document.getElementById('createLeaseBtn')?.removeAttribute('disabled')");
        }
        await FillLease(unitId, tenantId, today, today.AddDays(-10), "350", "0");
        await SubmitForm("#leaseCreateForm");
        Log(_page.Url.Contains("/Create") ? "PASS" : "FAIL", "Lease: end before start is rejected", $"{await Alerts()} / {await FieldErrors()} field errors ({await Shot("lease-bad-dates")})");

        // ── 6. Lease with deposit → Pending ──
        await FillLease(unitId, tenantId, today, today.AddYears(1).AddDays(-1), "350", "350");
        await _page.WaitForTimeoutAsync(300);
        var summaryOutcome = await _page.Locator("#sumOutcomeText").InnerTextAsync();
        Log(summaryOutcome.Contains("Pending") ? "PASS" : "NOTE", "Lease form summary predicts Pending when a deposit is set", $"{summaryOutcome} ({await Shot("lease-form-filled")})");
        await SubmitForm("#leaseCreateForm");
        Log(_page.Url.Contains("/Create") ? "FAIL" : "PASS", "Lease: create with deposit", $"url={_page.Url}; {await Alerts()} ({await Shot("lease-created")})");
        await _page.GotoAsync("/Leases");
        var lrow = _page.Locator($"tr.tenant-row[data-tenant-name*='{tenantLast.ToLower()}']").First;
        var leaseId = await lrow.CountAsync() > 0 ? await lrow.GetAttributeAsync("data-lease-id") : null;
        var leaseStatus = await lrow.CountAsync() > 0 ? await lrow.GetAttributeAsync("data-status") : null;
        Log(leaseId != null ? "PASS" : "FAIL", "Lease appears in the list", $"id={leaseId} status={leaseStatus}");
        Log(leaseStatus == "Pending" ? "PASS" : "FAIL", "Deposit lease starts as Pending", $"status={leaseStatus}");
        if (leaseId == null) { await Finish(serverErrors); return; }

        await _page.GotoAsync($"/Units?propertyId={propertyId}");
        var unitStatusCell = await _page.Locator("tr[data-search*='qa-101'] td[data-label='Status']").First.InnerTextAsync();
        Log("NOTE", "Unit status while lease is Pending", $"QA-101 shows '{unitStatusCell.Trim()}' (lease-aware guard blocks a second lease regardless)");

        // ── 7. Duplicate lease on the same unit ──
        await FillLease(unitId, tenantId, today, today.AddYears(1).AddDays(-1), "350", "0");
        var disabledOpt = await _page.Locator("#unitSelect option[disabled]").CountAsync();
        await SubmitForm("#leaseCreateForm");
        var conflict = await _page.Locator(".lw-conflict").CountAsync();
        Log(conflict > 0 && _page.Url.Contains("/Create") ? "PASS" : "FAIL", "Second lease on an already-leased unit is rejected",
            $"conflict banner={conflict}, unit option disabled in dropdown={disabledOpt > 0}; {await Alerts()} ({await Shot("lease-duplicate")})");

        // ── 8. Activate ──
        await _page.GotoAsync($"/Leases/Details/{leaseId}");
        var activate = _page.Locator("form[action$='/Leases/Activate/" + leaseId + "'] button[type='submit'], form[action*='Activate'] button[type='submit']").First;
        if (await activate.CountAsync() > 0)
        {
            await activate.ClickAsync(); await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            var a = await Alerts();
            Log(a.Contains("activated", StringComparison.OrdinalIgnoreCase) ? "PASS" : "FAIL", "Activate pending lease", $"{a} ({await Shot("lease-activated")})");
            var step = await _page.Locator(".lease-lifecycle .ll-step.current .ll-label").AllInnerTextsAsync();
            Log("NOTE", "Lifecycle stepper after activation", string.Join(",", step));
        }
        else Log("FAIL", "Activate button present on a Pending lease", "not found");

        await _page.GotoAsync($"/Invoices?leaseId={leaseId}");
        var depositRows = await _page.Locator("table tbody tr:has-text('Deposit')").CountAsync();
        Log(depositRows > 0 ? "PASS" : "FAIL", "Deposit invoice issued on activation", $"{depositRows} deposit invoice row(s) ({await Shot("invoices-after-activate")})");
        await _page.GotoAsync($"/Units?propertyId={propertyId}");
        unitStatusCell = await _page.Locator("tr[data-search*='qa-101'] td[data-label='Status']").First.InnerTextAsync();
        Log(unitStatusCell.Contains("Occupied", StringComparison.OrdinalIgnoreCase) ? "PASS" : "FAIL", "Unit becomes Occupied after activation", unitStatusCell.Trim());

        // ── 9. Terminate ──
        await _page.GotoAsync($"/Leases/Details/{leaseId}#terminate");
        var termForm = _page.Locator("form[action*='Terminate']").First;
        if (await termForm.CountAsync() > 0)
        {
            await termForm.Locator("input[name='terminationDate']").FillAsync(today.ToString("yyyy-MM-dd"));
            await termForm.Locator("textarea[name='reason']").FillAsync("QA: tester moved out");
            await termForm.Locator("button[type='submit']").First.ClickAsync();
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            Log("NOTE", "Terminate lease", $"{await Alerts()} ({await Shot("lease-terminated")})");
            await _page.GotoAsync($"/Units?propertyId={propertyId}");
            unitStatusCell = await _page.Locator("tr[data-search*='qa-101'] td[data-label='Status']").First.InnerTextAsync();
            Log(unitStatusCell.Contains("Vacant", StringComparison.OrdinalIgnoreCase) ? "PASS" : "FAIL", "Unit back to Vacant after termination", unitStatusCell.Trim());
        }
        else Log("FAIL", "Terminate form present on an Active lease", "not found");

        // ── 10. Re-lease the same unit (no deposit → Active) ──
        await FillLease(unitId, tenantId, today.AddDays(1), today.AddYears(1), "360", "0");
        await SubmitForm("#leaseCreateForm");
        Log(_page.Url.Contains("/Create") ? "FAIL" : "PASS", "Re-lease a terminated unit (no deposit)", $"{await Alerts()} ({await Shot("lease-released")})");
        await _page.GotoAsync($"/Units?propertyId={propertyId}");
        unitStatusCell = await _page.Locator("tr[data-search*='qa-101'] td[data-label='Status']").First.InnerTextAsync();
        Log(unitStatusCell.Contains("Occupied", StringComparison.OrdinalIgnoreCase) ? "PASS" : "FAIL", "Unit Occupied again after re-lease", unitStatusCell.Trim());

        // ── 11. Delete property that has units + leases ──
        await _page.GotoAsync($"/Properties/Delete/{propertyId}");
        var delBtn = _page.Locator("form[action*='Delete'] button[type='submit'], form[action*='Delete'] input[type='submit']").First;
        if (await delBtn.CountAsync() > 0)
        {
            await delBtn.ClickAsync(); await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            var a = await Alerts();
            await _page.GotoAsync("/Properties");
            var still = await _page.Locator($".lp-card[data-name='{propName}']").CountAsync();
            Log(still > 0 ? "PASS" : "FAIL", "Deleting a property with units/leases is refused", $"{a}; still listed={still > 0} ({await Shot("property-delete-refused")})");
        }
        else Log("NOTE", "Property delete", "no delete form rendered");

        await Finish(serverErrors);
    }

    private async Task Finish(List<string> serverErrors)
    {
        Log(serverErrors.Count == 0 ? "PASS" : "FAIL", "No HTTP 5xx during the journey", string.Join("; ", serverErrors.Distinct()));
        await File.WriteAllTextAsync(Path.Combine(_out, "report.md"), _report.ToString());
    }
}
