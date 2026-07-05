using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace RentCollection.E2ETests.Pages;

/// <summary>
/// Page object for <c>/Units/Create</c>. Deep-link <c>?propertyId=N</c>
/// pre-selects the property. New units default to Status = "Vacant".
/// Success redirects to <c>/Units/Details/{unitId}</c>.
/// </summary>
public sealed class UnitCreatePage
{
    private readonly IPage _page;
    public UnitCreatePage(IPage page) => _page = page;

    public Task GoToAsync(int? propertyId = null)
    {
        var url = "/Units/Create" + (propertyId is int p ? $"?propertyId={p}" : "");
        return _page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
    }

    public ILocator PropertyId => _page.Locator("#PropertyId");     // select
    public ILocator UnitNumber => _page.Locator("#UnitNumber");
    public ILocator UnitType => _page.Locator("#UnitType");         // select
    public ILocator MonthlyRent => _page.Locator("#MonthlyRent");
    public ILocator Bedrooms => _page.Locator("#Bedrooms");
    public ILocator Bathrooms => _page.Locator("#Bathrooms");
    public ILocator CreateButton => _page.Locator("button.btn-primary").First;

    /// <summary>Fill the required unit fields. PropertyId is pre-set by the deep-link.</summary>
    public async Task FillAsync(string unitNumber, decimal monthlyRent, string? propertyIdValue = null,
        string unitType = "Apartment", int bedrooms = 2, int bathrooms = 1)
    {
        if (propertyIdValue != null)
            await PropertyId.SelectOptionAsync(new SelectOptionValue { Value = propertyIdValue });
        await UnitNumber.FillAsync(unitNumber);
        if (await UnitType.CountAsync() > 0)
        {
            // UnitType is optional; tolerate a label/option mismatch.
            try { await UnitType.SelectOptionAsync(new SelectOptionValue { Label = unitType }, new() { Timeout = 4000 }); }
            catch (TimeoutException)
            {
                try { await UnitType.SelectOptionAsync(new SelectOptionValue { Index = 1 }, new() { Timeout = 4000 }); }
                catch (TimeoutException) { }
            }
        }
        await MonthlyRent.FillAsync(monthlyRent.ToString("0.000"));
        if (await Bedrooms.CountAsync() > 0) await Bedrooms.FillAsync(bedrooms.ToString());
        if (await Bathrooms.CountAsync() > 0) await Bathrooms.FillAsync(bathrooms.ToString());
        var floor = _page.Locator("#FloorNumber");
        if (await floor.CountAsync() > 0) await floor.FillAsync("2");
        var sqft = _page.Locator("#SquareFeet");
        if (await sqft.CountAsync() > 0) await sqft.FillAsync("850");
    }

    /// <summary>Submit and return the new UnitId parsed from the /Units/Details/{id} redirect (0 if not created).</summary>
    public async Task<int> SubmitAndGetUnitIdAsync(int timeoutMs = 20_000)
    {
        await CreateButton.ClickAsync(new() { NoWaitAfter = true });
        try { await _page.WaitForURLAsync(new Regex(@"/Units/Details/\d+"), new() { Timeout = timeoutMs }); }
        catch (TimeoutException) { return 0; }
        var m = Regex.Match(_page.Url, @"/Units/Details/(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }
}
