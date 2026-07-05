using Microsoft.Playwright;

namespace RentCollection.E2ETests.Pages;

/// <summary>
/// Page object for <c>/Account/Login</c>. Centralizes the selectors so a
/// markup tweak in <c>Login.cshtml</c> lands in one file, not ten tests.
/// </summary>
public sealed class LoginPage
{
    private readonly IPage _page;
    public LoginPage(IPage page) => _page = page;

    public Task GoToAsync() => _page.GotoAsync("/Account/Login").ContinueWith(_ => Task.CompletedTask);

    public ILocator EmailInput => _page.Locator("#Email");
    public ILocator PasswordInput => _page.Locator("#passwordInput");
    public ILocator SubmitButton => _page.GetByRole(AriaRole.Button, new() { Name = "Sign in" });
    public ILocator DemoCredentialsButton => _page.GetByRole(AriaRole.Button, new() { Name = "Fill demo credentials" });
    public ILocator ShowPasswordToggle => _page.Locator("#togglePassword");
    public ILocator TenantPortalLink => _page.GetByRole(AriaRole.Link, new() { Name = "Tenant portal login" });
    public ILocator ForgotPasswordLink => _page.GetByRole(AriaRole.Link, new() { Name = "Forgot password" });

    /// <summary>
    /// Fill the form with the supplied credentials and submit. Returns the
    /// page after navigation completes so the caller can assert on the
    /// landing URL or any redirect-target content.
    /// </summary>
    public async Task SubmitAsync(string email, string password)
    {
        await EmailInput.FillAsync(email);
        await PasswordInput.FillAsync(password);
        await SubmitButton.ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }
}
