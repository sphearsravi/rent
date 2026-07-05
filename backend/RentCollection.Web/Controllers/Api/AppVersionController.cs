using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RentCollection.Web.Controllers.Api;

/// <summary>
/// Force-update gate source. The tenant app calls this on splash (before
/// auth) and compares the running version against <c>minSupportedVersion</c>:
/// if older, it shows a non-dismissible "update required" screen.
///
/// Values come from configuration so a bad release can be locked out without
/// redeploying the API:
/// <code>
/// "AppVersion": {
///   "Android": { "MinSupported": "1.0.0", "Latest": "1.0.0", "StoreUrl": "https://play.google.com/store/apps/details?id=com.viijar.tenant" },
///   "Ios":     { "MinSupported": "1.0.0", "Latest": "1.0.0", "StoreUrl": "https://apps.apple.com/app/id000000000" }
/// }
/// </code>
/// Anonymous by design — it must answer before the user can log in.
/// </summary>
[ApiController]
[Route("api/app")]
[AllowAnonymous]
public class AppVersionController : ControllerBase
{
    private readonly IConfiguration _config;

    public AppVersionController(IConfiguration config) => _config = config;

    [HttpGet("version")]
    public IActionResult GetVersion([FromQuery] string platform = "android")
    {
        // Default to the most permissive values if config is absent so a
        // missing section never bricks the app at startup.
        var section = platform.Trim().ToLowerInvariant() == "ios"
            ? _config.GetSection("AppVersion:Ios")
            : _config.GetSection("AppVersion:Android");

        var minSupported = section["MinSupported"] ?? "1.0.0";
        var latest = section["Latest"] ?? minSupported;
        var storeUrl = section["StoreUrl"] ?? string.Empty;

        return Ok(new
        {
            success = true,
            data = new
            {
                minSupportedVersion = minSupported,
                latestVersion = latest,
                storeUrl
            }
        });
    }
}
