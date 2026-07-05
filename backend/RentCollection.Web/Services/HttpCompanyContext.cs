using System.Security.Claims;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;

namespace RentCollection.Web.Services;

/// <summary>
/// Resolves [ICompanyContext] from the authenticated user's claims. The
/// `companyId` and role claims are set at login time by `AccountController`.
/// </summary>
public class HttpCompanyContext : ICompanyContext
{
    private readonly IHttpContextAccessor _accessor;

    public HttpCompanyContext(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public int? CompanyId
    {
        get
        {
            var raw = _accessor.HttpContext?.User?.FindFirstValue("companyId");
            return int.TryParse(raw, out var id) ? id : null;
        }
    }

    /// <summary>
    /// Bypass the company-scope filter when:
    ///   - caller is unauthenticated (e.g. /login) — no claim, no filter,
    ///   - caller is a tenant (JWT has `tenantId` claim) — tenants are
    ///     scoped by ownership via IsAuthorizedForTenant, not by company,
    ///   - caller is in SuperAdmin role,
    ///   - caller is a legacy admin without a companyId claim.
    ///
    /// Without these bypasses the filter returns zero rows because every
    /// real Tenant/Property/User row has a non-null CompanyId.
    /// </summary>
    public bool IsSuperAdmin
    {
        get
        {
            var user = _accessor.HttpContext?.User;
            if (user == null) return true;                              // pre-auth pipeline

            // Tenant JWT — has tenantId claim, scoped by ownership.
            if (!string.IsNullOrEmpty(user.FindFirstValue("tenantId")))
                return true;

            // Genuinely unauthenticated request — let it through (the
            // `[Authorize]` attributes will reject sensitive endpoints).
            if (user.Identity?.IsAuthenticated != true) return true;

            if (user.IsInRole(Roles.SuperAdmin)) return true;

            // Legacy admin: authenticated but no company claim → treat as super.
            if (string.IsNullOrEmpty(user.FindFirstValue("companyId")))
                return true;

            return false;
        }
    }
}
