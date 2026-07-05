using Hangfire.Dashboard;
using RentCollection.Core.Entities;

namespace RentCollection.Web.Services;

/// <summary>
/// Locks the /hangfire dashboard to authenticated SuperAdmin/Admin users only.
/// Without this, the dashboard is publicly browsable on any deployment.
/// </summary>
public class HangfireAdminAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();
        if (http?.User?.Identity?.IsAuthenticated != true) return false;
        return http.User.IsInRole(Roles.SuperAdmin) || http.User.IsInRole(Roles.Admin);
    }
}
