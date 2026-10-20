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
    ///   - caller is in the SuperAdmin ROLE.
    ///
    /// SECURITY (audit C6): a missing `companyId` claim must NOT be treated as
    /// SuperAdmin. Previously any authenticated admin whose CompanyId was null
    /// (mis-provisioned / imported) silently bypassed EVERY company filter and
    /// saw all landlords' data. SuperAdmin is now determined strictly by the
    /// role claim. A non-super admin with no company claim resolves to a null
    /// CurrentCompanyId and correctly sees NO company-scoped rows (deny), which
    /// forces proper provisioning rather than granting cross-company access.
    /// </summary>
    public bool IsSuperAdmin
    {
        get
        {
            var user = _accessor.HttpContext?.User;
            if (user == null) return true;                              // pre-auth pipeline / migrations

            // Tenant JWT — has tenantId claim, scoped by ownership.
            if (!string.IsNullOrEmpty(user.FindFirstValue("tenantId")))
                return true;

            // Genuinely unauthenticated request — let it through (the
            // `[Authorize]` attributes reject sensitive endpoints anyway).
            if (user.Identity?.IsAuthenticated != true) return true;

            // SuperAdmin is decided ONLY by the role — never inferred from an
            // absent company claim.
            return user.IsInRole(Roles.SuperAdmin);
        }
    }
}
