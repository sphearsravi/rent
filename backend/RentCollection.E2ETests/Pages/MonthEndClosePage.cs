using Microsoft.Playwright;

namespace RentCollection.E2ETests.Pages;

/// <summary>
/// Page object for the Month-End Close checklist at
/// <c>/Reports/MonthEndClose</c>. Wraps the page's structural elements:
/// the period nav bar, the 4-card summary strip, the 9-step ordered list,
/// the closed banner, and the close-action section.
/// </summary>
public sealed class MonthEndClosePage
{
    private readonly IPage _page;
    public MonthEndClosePage(IPage page) => _page = page;

    public async Task GoToAsync(string? period = null)
    {
        var url = "/Reports/MonthEndClose" + (period != null ? $"?period={period}" : "");
        await _page.GotoAsync(url);
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    public ILocator StepsList => _page.Locator("#stepsList");
    public ILocator AllStepRows => _page.Locator("#stepsList > li");
    public ILocator GreenCount => _page.Locator("#sumGreen");
    public ILocator AmberCount => _page.Locator("#sumAmber");
    public ILocator RedCount   => _page.Locator("#sumRed");
    public ILocator MutedCount => _page.Locator("#sumMuted");
    public ILocator ClosedBanner => _page.Locator(".closed-banner, #closedBanner");
    public ILocator CloseButton  => _page.GetByRole(AriaRole.Button, new() { Name = "Mark period closed" });
    public ILocator NotesTextarea => _page.Locator("#closeNotes, textarea[name='notes']");

    /// <summary>
    /// Wait for the step list to render before assertions — the rows are
    /// hydrated by the page's JS from the embedded initialState, so a
    /// raw DOMContentLoaded isn't enough.
    /// </summary>
    public Task WaitForRenderedAsync() =>
        AllStepRows.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
}
