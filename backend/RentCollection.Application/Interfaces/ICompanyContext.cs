namespace RentCollection.Application.Interfaces;

/// <summary>
/// Per-request company scope. Resolved from the authenticated user's
/// CompanyId claim. EF Core query filters on company-scoped entities
/// (Property, Tenant, Lease, Invoice, Payment, Document, ...) read this
/// to silently filter rows to the current company.
///
/// `IsSuperAdmin == true` disables the filter so cross-company admins
/// can see everything.
/// </summary>
public interface ICompanyContext
{
    int? CompanyId { get; }
    bool IsSuperAdmin { get; }
}
