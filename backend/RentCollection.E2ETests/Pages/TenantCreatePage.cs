using Microsoft.Playwright;

namespace RentCollection.E2ETests.Pages;

/// <summary>
/// Page object for <c>/Tenants/Create</c> (Sprint 23 revamp). Centralizes the
/// form's selectors and workflows so a markup tweak lands in one file.
///
/// Selector gotchas this page object hides from tests:
///   • The Client Civil ID (PACI) field's id is <c>#civilIdInput</c>, NOT
///     <c>#PACINumber</c> — it also enforces exactly 12 digits.
///   • <c>#CompanyId</c> is a HIDDEN input auto-assigned from the admin's
///     company claim, not a &lt;select&gt;. <see cref="SetCompanyIdAsync"/>
///     sets it directly.
///   • DOB / Government ID / emergency fields live in the collapsed
///     "More details" zone — call <see cref="ExpandMoreDetailsAsync"/> first.
///   • The primary submit is <c>button.fr-btn-primary</c> ("Create Tenant");
///     there's also a secondary "Save &amp; add another".
///   • The Google Gemini "Scan card" option was removed — the
///     <see cref="ScanCardButton"/> / <see cref="GeminiConsentCheckbox"/>
///     locators exist only so tests can assert they are absent.
/// </summary>
public sealed class TenantCreatePage
{
    private readonly IPage _page;
    public TenantCreatePage(IPage page) => _page = page;

    public const string Path = "/Tenants/Create";

    // ── Navigation ───────────────────────────────────────────────────────
    public async Task GoToAsync()
    {
        await _page.GotoAsync(Path, new() { WaitUntil = WaitUntilState.NetworkIdle });
    }

    // ── Structural ───────────────────────────────────────────────────────
    public ILocator Form => _page.Locator("#tenantCreateForm");
    public ILocator ValidationSummary => _page.Locator("div[asp-validation-summary], .alert-danger").First;

    // ── Essentials (always visible, required) ────────────────────────────
    public ILocator FirstName => _page.Locator("#FirstName");
    public ILocator LastName => _page.Locator("#LastName");
    public ILocator Email => _page.Locator("#Email");
    public ILocator Phone => _page.Locator("#Phone");
    /// <summary>Client Civil ID (PACI) — 12-digit field, id is #civilIdInput.</summary>
    public ILocator Paci => _page.Locator("#civilIdInput");
    public ILocator BuildingPaci => _page.Locator("#BuildingPACINumber");
    public ILocator CivilIdStatus => _page.Locator("#civilIdStatus");
    /// <summary>Hidden, auto-assigned company FK.</summary>
    public ILocator CompanyIdHidden => _page.Locator("#CompanyId");

    // ── "More details" (collapsed) ───────────────────────────────────────
    public ILocator MoreDetailsToggle => _page.Locator("[data-fr-toggle='moreDetails']");
    public ILocator DateOfBirth => _page.Locator("#DateOfBirth");
    public ILocator GovernmentId => _page.Locator("#GovernmentId");
    public ILocator EmploymentStatus => _page.Locator("#EmploymentStatus");
    public ILocator EmergencyContact => _page.Locator("#EmergencyContact");
    public ILocator EmergencyPhone => _page.Locator("#EmergencyPhone");
    public ILocator PropertySelect => _page.Locator("#PropertyId");
    public ILocator UnitSelect => _page.Locator("#UnitId");

    // ── Actions ──────────────────────────────────────────────────────────
    public ILocator CreateButton => _page.Locator("button.fr-btn-primary").First;
    public ILocator SaveAndAddButton => _page.Locator("[data-fr-save-and-add]");
    public ILocator ClearAllButton => _page.Locator("#clearAllBtn");
    public ILocator CancelLink => _page.GetByRole(AriaRole.Link, new() { Name = "Cancel" });

    // ── Removed Gemini scan (assert-absent only) ─────────────────────────
    public ILocator ScanCardButton => _page.Locator("#scanCivilIdBtn");
    public ILocator GeminiConsentCheckbox => _page.Locator("#civilIdConsent");
    public ILocator CivilIdImageFile => _page.Locator("#civilIdImageFile");

    // ── Workflows ────────────────────────────────────────────────────────

    /// <summary>Expand the collapsed "More details" zone so DOB/emergency fields interact.</summary>
    public Task ExpandMoreDetailsAsync() => MoreDetailsToggle.ClickAsync();

    /// <summary>Set the hidden, auto-assigned CompanyId directly (default company 1).</summary>
    public Task SetCompanyIdAsync(string companyId = "1") =>
        _page.EvaluateAsync("(v) => { var c = document.getElementById('CompanyId'); if (c) c.value = v; }", companyId);

    /// <summary>Fill the required Essentials. PACI must be exactly 12 digits.</summary>
    public async Task FillEssentialsAsync(string firstName, string lastName, string email,
        string phone, string paci, string buildingPaci, string companyId = "1")
    {
        await FirstName.FillAsync(firstName);
        await LastName.FillAsync(lastName);
        await Email.FillAsync(email);
        await Phone.FillAsync(phone);
        await Paci.FillAsync(paci);
        await BuildingPaci.FillAsync(buildingPaci);
        await SetCompanyIdAsync(companyId);
    }

    /// <summary>Expand "More details" and fill the optional tenant fields too.</summary>
    public async Task FillOptionalAsync(string civilId)
    {
        if (await MoreDetailsToggle.CountAsync() > 0) await MoreDetailsToggle.ClickAsync();
        await FillIfPresent("#DateOfBirth", "1990-05-20");
        await FillIfPresent("#GovernmentId", civilId);
        if (await EmploymentStatus.CountAsync() > 0)
        {
            try { await EmploymentStatus.SelectOptionAsync(new SelectOptionValue { Label = "Employed" }, new() { Timeout = 4000 }); }
            catch (TimeoutException) { }
        }
        await FillIfPresent("#EmergencyContact", "Next Of Kin");
        await FillIfPresent("#EmergencyPhone", "99887766");
    }

    private async Task FillIfPresent(string selector, string value)
    {
        var l = _page.Locator(selector);
        if (await l.CountAsync() == 0) return;
        try { await l.ScrollIntoViewIfNeededAsync(); await l.FillAsync(value, new() { Timeout = 4000 }); }
        catch (TimeoutException) { }
    }

    /// <summary>
    /// Ask the browser whether the form would pass its own HTML5 validation.
    /// Returns "VALID", or "INVALID -&gt; field: message | …" naming each
    /// blocking control (catches hidden required-field traps).
    /// </summary>
    public Task<string> CheckHtml5ValidityAsync() => _page.EvaluateAsync<string>(@"() => {
        var f = document.getElementById('tenantCreateForm');
        if (!f) return 'NO_FORM';
        if (f.checkValidity()) return 'VALID';
        return 'INVALID -> ' + [...f.elements].filter(e => e.willValidate && !e.checkValidity())
            .map(e => (e.name||e.id) + ': ' + e.validationMessage).join(' | ');
    }");

    /// <summary>
    /// Select a Property in the (collapsed) assignment zone by its id value.
    /// The dropdown is hydrated async from /api/properties, and SelectOption
    /// retries until the option appears — no explicit wait needed.
    /// </summary>
    public Task SelectPropertyAsync(string propertyIdValue) =>
        PropertySelect.SelectOptionAsync(new SelectOptionValue { Value = propertyIdValue });

    /// <summary>
    /// Select a Unit by its id value. The unit list is populated + enabled by
    /// the property-change handler (vacant units only); SelectOption waits for
    /// the control to be enabled and the option to exist.
    /// </summary>
    public Task SelectUnitAsync(string unitIdValue) =>
        UnitSelect.SelectOptionAsync(new SelectOptionValue { Value = unitIdValue });

    /// <summary>Clear All — wipe every field. Returns after the click settles.</summary>
    public Task ClickClearAllAsync() => ClearAllButton.ClickAsync();

    /// <summary>
    /// Submit via "Create Tenant" and wait for the redirect off /Tenants/Create.
    /// Uses NoWaitAfter + WaitForURL because the multipart POST redirects to
    /// /Tenants, whose KPI ajax otherwise stalls Playwright's auto-wait.
    /// Returns the landed URL (still on Create means the submit was rejected).
    /// </summary>
    public async Task<string> SubmitAsync(int timeoutMs = 20_000)
    {
        await CreateButton.ClickAsync(new() { NoWaitAfter = true });
        try { await _page.WaitForURLAsync(u => !u.Contains("/Tenants/Create"), new() { Timeout = timeoutMs }); }
        catch (TimeoutException) { /* caller inspects the returned URL */ }
        return _page.Url;
    }
}
