using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace RentCollection.Web.Controllers;

/// <summary>
/// Sets the UI language via the culture cookie, then returns the user to the
/// page they were on. AllowAnonymous so the switcher also works on the login page.
/// </summary>
[AllowAnonymous]
public class CultureController : Controller
{
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public IActionResult Set(string culture, string? returnUrl = null)
    {
        if (culture is "en-US" or "ar-KW")
        {
            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    IsEssential = true,
                    Path = "/",
                });
        }
        // Only redirect to a safe local path; anything else falls back to home.
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }
}
