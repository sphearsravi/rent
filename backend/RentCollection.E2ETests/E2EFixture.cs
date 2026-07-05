using Microsoft.Playwright;

namespace RentCollection.E2ETests;

/// <summary>
/// Class fixture that owns the Playwright + Chromium instance for the test
/// class. Shared across tests in the class so we boot one browser, not one
/// per test (Chromium startup ≈ 1.5 s — quickly dominates the suite otherwise).
///
/// Each test gets a fresh <see cref="IBrowserContext"/> via
/// <see cref="NewContextAsync"/> so cookies/storage are isolated — sign-in
/// state from one test doesn't leak into the next.
/// </summary>
public sealed class E2EFixture : IAsyncLifetime
{
    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;

    /// <summary>
    /// Base URL of the running backend. Overridable via the
    /// <c>RC_E2E_BASE_URL</c> env var so a CI job can point at a deployed
    /// stage environment without rebuilding the tests.
    /// </summary>
    public string BaseUrl { get; } =
        Environment.GetEnvironmentVariable("RC_E2E_BASE_URL") ?? "http://localhost:5032";

    /// <summary>Slow-mo (ms) inserted between Playwright actions when debugging.</summary>
    private static readonly int SlowMoMs =
        int.TryParse(Environment.GetEnvironmentVariable("RC_E2E_SLOWMO"), out var v) ? v : 0;

    /// <summary>Set <c>RC_E2E_HEADED=1</c> to watch the browser for debugging.</summary>
    private static readonly bool Headless =
        Environment.GetEnvironmentVariable("RC_E2E_HEADED") != "1";

    public async Task InitializeAsync()
    {
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = Headless,
            SlowMo = SlowMoMs,
        });
    }

    public async Task DisposeAsync()
    {
        await Browser.CloseAsync();
        Playwright.Dispose();
    }

    /// <summary>
    /// Create a clean browser context (fresh cookies, no service workers).
    /// Each test should use <c>await using</c> so the context disposes
    /// when the test method returns even if it throws.
    /// </summary>
    /// <summary>Set RC_E2E_VIDEO=1 (CI) to record a .webm per context for debugging failures.</summary>
    private static readonly string? VideoDir =
        Environment.GetEnvironmentVariable("RC_E2E_VIDEO") == "1" ? "videos" : null;

    public Task<IBrowserContext> NewContextAsync(BrowserNewContextOptions? options = null)
    {
        options ??= new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ViewportSize = new ViewportSize { Width = 1366, Height = 800 },
            IgnoreHTTPSErrors = true,
        };
        // Record video when enabled (CI). Auto-saved when the context disposes,
        // so it's available as a build artifact after a failed run.
        if (VideoDir != null)
        {
            options.RecordVideoDir = VideoDir;
            options.RecordVideoSize = new RecordVideoSize { Width = 1366, Height = 800 };
        }
        return Browser.NewContextAsync(options);
    }
}
