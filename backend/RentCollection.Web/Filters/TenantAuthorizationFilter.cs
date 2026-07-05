using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RentCollection.Web.Filters;

/// <summary>
/// Authorization filter to ensure only tenants can access tenant portal pages
/// </summary>
public class TenantAuthorizationFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // Check if action has AllowAnonymous attribute
        if (context.ActionDescriptor.EndpointMetadata.Any(em => em is IAllowAnonymous))
        {
            return; // Skip authorization for actions marked with [AllowAnonymous]
        }

        var tenantId = context.HttpContext.Session.GetInt32("TenantId");

        if (!tenantId.HasValue)
        {
            // Not logged in as tenant - redirect to tenant login
            context.Result = new RedirectToActionResult("Login", "TenantPortal", null);
        }
    }
}

/// <summary>
/// Authorization filter to ensure tenants CANNOT access admin pages
/// </summary>
public class AdminAuthorizationFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var tenantId = context.HttpContext.Session.GetInt32("TenantId");

        if (tenantId.HasValue)
        {
            // Logged in as tenant - redirect to tenant dashboard
            context.Result = new RedirectToActionResult("Dashboard", "TenantPortal", null);
        }
    }
}
