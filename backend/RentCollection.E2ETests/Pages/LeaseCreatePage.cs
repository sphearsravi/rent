using Microsoft.Playwright;

namespace RentCollection.E2ETests.Pages;

/// <summary>
/// Page object for <c>/Leases/Create</c>. Deep-link <c>?unitId=N</c> pre-selects
/// the building + unit and prefills MonthlyRent + dates (via the cascade JSON).
/// Building/Unit/Tenant are Select2 widgets, so we set the underlying native
/// select value and fire a change event (Select2 listens to it).
///
/// Status rule: SecurityDeposit == 0 → the new lease is created ACTIVE (and the
/// unit flips to Occupied) with no separate activation step.
/// </summary>
public sealed class LeaseCreatePage
{
    private readonly IPage _page;
    public LeaseCreatePage(IPage page) => _page = page;

    public Task GoToAsync(int unitId) =>
        _page.GotoAsync($"/Leases/Create?unitId={unitId}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });

    public ILocator BuildingSelect => _page.Locator("#buildingSelect");
    public ILocator UnitSelect => _page.Locator("#unitSelect");
    public ILocator TenantSelect => _page.Locator("#tenantSelect");
    public ILocator LeaseStartDate => _page.Locator("#LeaseStartDate");
    public ILocator LeaseEndDate => _page.Locator("#LeaseEndDate");
    public ILocator MonthlyRent => _page.Locator("#MonthlyRent");
    public ILocator SecurityDeposit => _page.Locator("#SecurityDeposit");
    public ILocator CreateButton => _page.Locator("button.fr-btn-primary").First;

    /// <summary>Set a Select2-backed native select by value and notify Select2/jQuery.</summary>
    private Task SetSelect2Async(string elementId, string value) => _page.EvaluateAsync(
        @"({id, val}) => {
            const s = document.getElementById(id);
            if (!s) return;
            s.value = val;
            s.dispatchEvent(new Event('change', { bubbles: true }));
            if (window.jQuery) { window.jQuery(s).val(val).trigger('change'); }
        }", new { id = elementId, val = value });

    /// <summary>
    /// Fill the lease form for a deep-linked unit: pick the tenant, force a
    /// start date (so the monthly-invoice job includes it), and set the deposit.
    /// </summary>
    public async Task FillAsync(string tenantIdValue, DateTime startDate, decimal monthlyRent,
        decimal securityDeposit = 0m)
    {
        await SetSelect2Async("tenantSelect", tenantIdValue);
        await LeaseStartDate.FillAsync(startDate.ToString("yyyy-MM-dd"));
        await LeaseEndDate.FillAsync(startDate.AddYears(1).AddDays(-1).ToString("yyyy-MM-dd"));
        await MonthlyRent.FillAsync(monthlyRent.ToString("0.000"));   // ensure > 0 even if prefill missed
        await SecurityDeposit.FillAsync(securityDeposit.ToString("0.000"));
    }

    /// <summary>
    /// Expand "More" and fill the optional lease fields (payment day, late fee,
    /// grace period, type). Insurance is deliberately left at 0 so the payment
    /// flow (single rent invoice) is unchanged.
    /// </summary>
    public async Task FillOptionalAsync()
    {
        var toggle = _page.Locator("[data-fr-toggle='moreLease']");
        if (await toggle.CountAsync() > 0) await toggle.ClickAsync();
        await FillIfPresent("#PaymentDueDay", "1");
        await FillIfPresent("#LateFeeAmount", "10.000");    // step=0.001 → 3 decimals ok
        await FillIfPresent("#LateFeeGracePeriod", "5");
        var lt = _page.Locator("#LeaseType");
        if (await lt.CountAsync() > 0)
        {
            try { await lt.SelectOptionAsync(new SelectOptionValue { Label = "Fixed" }, new() { Timeout = 4000 }); }
            catch (TimeoutException)
            {
                try { await lt.SelectOptionAsync(new SelectOptionValue { Index = 1 }, new() { Timeout = 4000 }); }
                catch (TimeoutException) { }
            }
        }
    }

    private async Task FillIfPresent(string selector, string value)
    {
        var l = _page.Locator(selector);
        if (await l.CountAsync() == 0) return;
        try { await l.ScrollIntoViewIfNeededAsync(); await l.FillAsync(value, new() { Timeout = 4000 }); }
        catch (TimeoutException) { }
    }

    /// <summary>Submit and wait for the redirect off /Leases/Create. Returns landed URL.</summary>
    public async Task<string> SubmitAsync(int timeoutMs = 20_000)
    {
        await CreateButton.ClickAsync(new() { NoWaitAfter = true });
        try { await _page.WaitForURLAsync(u => !u.Contains("/Leases/Create"), new() { Timeout = timeoutMs }); }
        catch (TimeoutException) { }
        return _page.Url;
    }
}
