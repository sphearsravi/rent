namespace RentCollection.Core.Entities;

public class User
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string Role { get; set; } = Roles.User; // see Roles
    public int? CompanyId { get; set; }  // null = global (SuperAdmin)
    public bool IsActive { get; set; } = true;
    public bool EmailVerified { get; set; } = false;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedDate { get; set; }
    public DateTime? LastLogin { get; set; }

    // MFA (TOTP) — wired in Item 12.
    public bool MfaEnabled { get; set; } = false;
    public string? MfaSecret { get; set; }
}

/// <summary>
/// Canonical role names. Use these constants in [Authorize(Roles = ...)] so
/// the compiler catches typos and the IDE can find usages.
/// </summary>
public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";   // cross-company, full access
    public const string Admin = "Admin";             // single company, full access
    public const string Manager = "Manager";         // property manager — tenants, leases, maintenance
    public const string Accountant = "Accountant";   // invoices, payments, reports — read+write
    public const string User = "User";               // read-only viewer
    public const string Tenant = "Tenant";           // tenant app user — never granted to web users

    /// Used in [Authorize(Roles = Roles.AnyAdmin)] — anyone who can administer.
    public const string AnyAdmin = SuperAdmin + "," + Admin;
    /// Anyone who can manage operational data (tenants/leases/maintenance).
    public const string Operational = SuperAdmin + "," + Admin + "," + Manager;
    /// Anyone who can touch money.
    public const string Financial = SuperAdmin + "," + Admin + "," + Accountant;

    /// <summary>
    /// Roles that can be granted to a back-office (web) <see cref="User"/>.
    /// <c>Tenant</c> is deliberately excluded — it belongs to the tenant
    /// portal user flow and granting it to a staff account would let them
    /// authenticate through the tenant endpoints by mistake.
    /// </summary>
    public static readonly string[] AssignableToWebUsers =
        { SuperAdmin, Admin, Manager, Accountant, User };

    /// <summary>
    /// Roles a non-SuperAdmin <see cref="Admin"/> can grant. Promoting
    /// someone to SuperAdmin requires being a SuperAdmin yourself —
    /// enforced in <c>UsersController</c> safeguards.
    /// </summary>
    public static readonly string[] GrantableByAdmin =
        { Admin, Manager, Accountant, User };
}
