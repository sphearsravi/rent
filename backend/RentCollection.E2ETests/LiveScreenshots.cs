using Microsoft.Playwright;
using Xunit;

namespace RentCollection.E2ETests;

/// <summary>
/// Design-review helper, not a test of behaviour: signs in as an admin and
/// saves a full-page screenshot of each admin list page so a redesign can be
/// compared before/after. Opt-in only — runs when <c>RC_SHOT_BASE_URL</c>,
/// <c>RC_SHOT_EMAIL</c> and <c>RC_SHOT_PASS</c> are set; otherwise it is a
/// no-op so CI never needs credentials.
///
///   RC_SHOT_BASE_URL=https://www.alrtam.co RC_SHOT_EMAIL=… RC_SHOT_PASS=… \
///   dotnet test RentCollection.E2ETests --filter "Category=Screenshot"
///
/// Output: C:\Temp\shots\&lt;page&gt;.png (or RC_SHOT_OUT).
/// </summary>
[Trait("Category", "Screenshot")]
public sealed class LiveScreenshots : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fx;
    public LiveScreenshots(E2EFixture fx) => _fx = fx;

    private static readonly (string Name, string Path)[] Pages =
    {
        ("properties", "/Properties"),
        ("tenants", "/Tenants"),
        ("units", "/Units"),
        ("leases", "/Leases"),
        ("payments", "/Payments"),
        ("reports", "/Reports"),
        ("dashboard", "/Dashboard"),
        // Lease form with a unit deep-linked: proves the summary / cascade JS runs.
        ("lease-create", "/Leases/Create?unitId=759"),
        ("cash-record", "/CashPayment/Record"),
        ("cash-record-tenant", "/CashPayment/Record?tenantId=739"),
        ("cash-record-nothing-open", "/CashPayment/Record?tenantId=716"),
        ("property-details", "/Properties/Details/76"),
        ("tenant-details", "/Tenants/Details/735"),
        ("unit-details", "/Units/Details/759"),
        ("lease-details", "/Leases/Details/706"),
        ("invoice-details", "/Invoices/Details/1426"),
        ("payment-details", "/Payments/Details/126"),
        ("company-settings", "/CompanySettings"),
        ("approval-queue", "/PaymentApproval"),
    };

    /// <summary>Computed-style probe for an element: RC_SHOT_PROBE="/Dashboard|.od-toolbar .od-btn".</summary>
    private static async Task ProbeAsync(IPage page, string outDir)
    {
        var probe = Environment.GetEnvironmentVariable("RC_SHOT_PROBE");
        if (string.IsNullOrEmpty(probe) || !probe.Contains('|')) return;
        var path = probe.Split('|')[0]; var sel = probe.Split('|')[1];
        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        var json = await page.EvaluateAsync<string>(@"(sel) => {
            const el = document.querySelector(sel); if (!el) return 'not found: ' + sel;
            const cs = getComputedStyle(el); const r = el.getBoundingClientRect();
            const kids = Array.from(el.children).map(k => k.tagName + ' ' + JSON.stringify(k.getBoundingClientRect().width) + 'x' + JSON.stringify(k.getBoundingClientRect().height) + ' color=' + getComputedStyle(k).color + ' display=' + getComputedStyle(k).display);
            return JSON.stringify({ text: el.innerText, html: el.innerHTML.slice(0, 200), color: cs.color, bg: cs.backgroundColor, fontSize: cs.fontSize, opacity: cs.opacity, visibility: cs.visibility, textIndent: cs.textIndent, overflow: cs.overflow, width: r.width, height: r.height, kids }, null, 1);
        }", sel);
        await File.WriteAllTextAsync(Path.Combine(outDir, "probe.json"), json);
    }

    [Fact]
    public async Task Capture_admin_list_pages()
    {
        var baseUrl = Environment.GetEnvironmentVariable("RC_SHOT_BASE_URL");
        var email = Environment.GetEnvironmentVariable("RC_SHOT_EMAIL");
        var pass = Environment.GetEnvironmentVariable("RC_SHOT_PASS");
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(pass))
            return; // opt-in only

        var outDir = Environment.GetEnvironmentVariable("RC_SHOT_OUT") ?? @"C:\Temp\shots";
        Directory.CreateDirectory(outDir);
        var width = int.TryParse(Environment.GetEnvironmentVariable("RC_SHOT_WIDTH"), out var w) ? w : 1440;
        var culture = Environment.GetEnvironmentVariable("RC_SHOT_CULTURE"); // e.g. "ar"

        await using var context = await _fx.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = baseUrl,
            ViewportSize = new ViewportSize { Width = width, Height = 900 },
            Locale = culture == "ar" ? "ar-KW" : "en-US",
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Account/Login");
        await page.FillAsync("input[name='Email']", email);
        await page.FillAsync("input[name='Password']", pass);
        // The login page also carries a language-switch form; submit the
        // form that owns the Email field rather than the first submit button.
        await page.Locator("form:has(input[name='Email']) button[type='submit'], form:has(input[name='Email']) input[type='submit']").First.ClickAsync();
        await page.WaitForURLAsync(u => !u.Contains("/Account/Login"), new PageWaitForURLOptions { Timeout = 30000 });

        if (culture == "ar")
        {
            // Culture cookie, same name ASP.NET's CookieRequestCultureProvider reads.
            await context.AddCookiesAsync(new[]
            {
                new Cookie { Name = ".AspNetCore.Culture", Value = "c=ar|uic=ar", Url = baseUrl },
            });
        }

        await ProbeAsync(page, outDir);
        foreach (var (name, path) in Pages)
        {
            await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 45000 });
            await page.WaitForTimeoutAsync(800); // let skeleton KPIs resolve
            var suffix = (culture == "ar" ? "-ar" : "") + (width != 1440 ? $"-{width}" : "");
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(outDir, $"{name}{suffix}.png"),
                FullPage = true,
            });
        }
    }
}
