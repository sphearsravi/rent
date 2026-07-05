using Microsoft.Playwright;

namespace RentCollection.E2ETests.Pages;

/// <summary>
/// Page object for <c>/Properties/Create</c>. Note: unlike the Tenant form,
/// CompanyId here is a visible required &lt;select&gt; (not a hidden derived
/// field). Address/Area/Block are NOT NULL columns coalesced server-side.
/// </summary>
public sealed class PropertyCreatePage
{
    private readonly IPage _page;
    public PropertyCreatePage(IPage page) => _page = page;

    public const string Path = "/Properties/Create";
    public Task GoToAsync() => _page.GotoAsync(Path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });

    public ILocator PropertyName => _page.Locator("#PropertyName");
    public ILocator PaciNumber => _page.Locator("#PACINumber");
    public ILocator PropertyType => _page.Locator("#PropertyType");     // select
    public ILocator Governorate => _page.Locator("#Governorate");        // select
    public ILocator NumberOfUnits => _page.Locator("#NumberOfUnits");
    public ILocator CompanyId => _page.Locator("#CompanyId");            // required select
    public ILocator CreateButton => _page.Locator("button.fr-btn-primary").First;

    /// <summary>Fill the required Essentials. Type/Governorate default to valid options.</summary>
    public async Task FillRequiredAsync(string name, string paci, int numberOfUnits = 1,
        string type = "Apartment", string governorate = "Al Asimah", string companyIdValue = "1")
    {
        await PropertyName.FillAsync(name);
        await PaciNumber.FillAsync(paci);
        await PropertyType.SelectOptionAsync(new SelectOptionValue { Label = type });
        await Governorate.SelectOptionAsync(new SelectOptionValue { Label = governorate });
        await NumberOfUnits.FillAsync(numberOfUnits.ToString());
        await CompanyId.SelectOptionAsync(new SelectOptionValue { Value = companyIdValue });
    }

    /// <summary>Expand "More details" and fill the optional property fields too.</summary>
    public async Task FillOptionalAsync()
    {
        var toggle = _page.Locator("[data-fr-toggle='moreProp']");
        if (await toggle.CountAsync() > 0) await toggle.ClickAsync();
        await FillIfPresent("#Address", "Block 4, Street 12, Building 7");
        await FillIfPresent("#Area", "Salmiya");
        await FillIfPresent("#Block", "4");
        await FillIfPresent("#Street", "12");
        await FillIfPresent("#BuildingNumber", "7");
        await FillIfPresent("#PropertyValue", "250000");      // integer — matches step=0.01
        await FillIfPresent("#PurchaseDate", "2020-01-15");
    }

    private async Task FillIfPresent(string selector, string value)
    {
        var l = _page.Locator(selector);
        if (await l.CountAsync() == 0) return;
        try { await l.ScrollIntoViewIfNeededAsync(); await l.FillAsync(value, new() { Timeout = 4000 }); }
        catch (TimeoutException) { /* collapsed/not-editable — skip */ }
    }

    /// <summary>Submit and wait for the redirect to /Properties. Returns landed URL.</summary>
    public async Task<string> SubmitAsync(int timeoutMs = 20_000)
    {
        await CreateButton.ClickAsync(new() { NoWaitAfter = true });
        try { await _page.WaitForURLAsync(u => !u.Contains("/Properties/Create"), new() { Timeout = timeoutMs }); }
        catch (TimeoutException) { }
        return _page.Url;
    }
}
