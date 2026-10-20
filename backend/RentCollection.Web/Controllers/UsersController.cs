using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RentCollection.Application.Interfaces;
using RentCollection.Application.Services;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.Controllers;

[Authorize(Roles = Roles.AnyAdmin)]
public class UsersController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IAuditLogService _audit;
    private readonly ILogger<UsersController> _logger;
    private readonly IPasswordHasher _passwordHasher;

    public UsersController(
        ApplicationDbContext context,
        IAuditLogService audit,
        ILogger<UsersController> logger)
    {
        _context = context;
        _audit = audit;
        _logger = logger;
        _passwordHasher = new PasswordHasher();
    }

    // ── Role-assignment safeguards (Phase A of role-access plan) ──
    //
    // Three rules apply on every Role mutation, in both /Users/Edit and
    // /Users/ChangeRole. Returning null = ok; non-null = error message
    // ready to flash. The user can never:
    //   1. demote / delete themselves (no foot-shooting)
    //   2. demote / delete the LAST SuperAdmin (no lockout)
    //   3. grant the SuperAdmin role unless they ARE a SuperAdmin
    //
    private async Task<string?> ValidateRoleChangeAsync(User target, string newRole)
    {
        var actorName = User.Identity?.Name;
        var actorIsSuperAdmin = User.IsInRole(Roles.SuperAdmin);

        // 1. Don't change your own role.
        if (string.Equals(actorName, target.Username, StringComparison.OrdinalIgnoreCase)
            || string.Equals(actorName, target.Email, StringComparison.OrdinalIgnoreCase))
        {
            // Allow no-op (same role) for completeness — only block actual mutation.
            if (!string.Equals(target.Role, newRole, StringComparison.Ordinal))
                return "You cannot change your own role.";
        }

        // 2. Only SuperAdmin can grant SuperAdmin.
        if (newRole == Roles.SuperAdmin && !actorIsSuperAdmin)
            return "Only a SuperAdmin can grant the SuperAdmin role.";

        // 3. Don't demote the last SuperAdmin specifically — that role can
        //    only be granted by another SuperAdmin, so losing the last one
        //    is irrecoverable from inside the app.
        if (target.Role == Roles.SuperAdmin && newRole != Roles.SuperAdmin)
        {
            var superAdminCount = await _context.Users
                .CountAsync(u => u.Role == Roles.SuperAdmin && u.IsActive);
            if (superAdminCount <= 1)
                return "Cannot demote the last active SuperAdmin — the system would have no super-admin.";
        }

        // 4. Don't leave the system with zero admin-tier users (SuperAdmin
        //    or Admin). Without one, nobody can manage users or grant roles.
        var targetIsAdminTier = target.Role == Roles.SuperAdmin || target.Role == Roles.Admin;
        var newIsAdminTier = newRole == Roles.SuperAdmin || newRole == Roles.Admin;
        if (targetIsAdminTier && !newIsAdminTier)
        {
            var adminTierCount = await _context.Users
                .CountAsync(u => (u.Role == Roles.SuperAdmin || u.Role == Roles.Admin) && u.IsActive);
            if (adminTierCount <= 1)
                return "Cannot demote the last active admin — at least one Admin or SuperAdmin must remain.";
        }

        // 4. Validate the role is one of the assignable values.
        if (!Roles.AssignableToWebUsers.Contains(newRole))
            return $"'{newRole}' is not a valid web-user role.";

        return null;
    }

    private Task<string?> ValidateDeleteAsync(User target) => ValidateRemovalAsync(target, "delete");

    /// <summary>
    /// Deactivating (IsActive=false) removes the account from service just like a
    /// delete does, so the same self / last-SuperAdmin / last-admin guards apply.
    /// </summary>
    private Task<string?> ValidateDeactivateAsync(User target) => ValidateRemovalAsync(target, "deactivate");

    private async Task<string?> ValidateRemovalAsync(User target, string verb)
    {
        var actorName = User.Identity?.Name;
        if (string.Equals(actorName, target.Username, StringComparison.OrdinalIgnoreCase)
            || string.Equals(actorName, target.Email, StringComparison.OrdinalIgnoreCase))
            return $"You cannot {verb} your own account.";

        if (target.Role == Roles.SuperAdmin)
        {
            var superAdminCount = await _context.Users
                .CountAsync(u => u.Role == Roles.SuperAdmin && u.IsActive);
            if (superAdminCount <= 1)
                return $"Cannot {verb} the last active SuperAdmin.";
        }

        // Same admin-tier floor as ValidateRoleChangeAsync — never end up
        // with zero usable admin accounts.
        if (target.Role == Roles.SuperAdmin || target.Role == Roles.Admin)
        {
            var adminTierCount = await _context.Users
                .CountAsync(u => (u.Role == Roles.SuperAdmin || u.Role == Roles.Admin) && u.IsActive);
            if (adminTierCount <= 1)
                return $"Cannot {verb} the last active admin — at least one Admin or SuperAdmin must remain.";
        }
        return null;
    }

    /// <summary>
    /// Username and Email are login identifiers (AccountController looks them up
    /// across ALL companies), so uniqueness must be global and case-insensitive —
    /// hence IgnoreQueryFilters. Adds ModelState errors; returns true when unique.
    /// </summary>
    private async Task<bool> ValidateUniqueIdentityAsync(string? username, string? email, int? excludeUserId)
    {
        var ok = true;
        var uname = (username ?? string.Empty).Trim().ToLower();
        var mail = (email ?? string.Empty).Trim().ToLower();

        if (uname.Length > 0 && await _context.Users.IgnoreQueryFilters()
                .AnyAsync(u => u.Username.ToLower() == uname && (excludeUserId == null || u.UserId != excludeUserId)))
        {
            ModelState.AddModelError("Username", "That username is already taken.");
            ok = false;
        }
        if (mail.Length > 0 && await _context.Users.IgnoreQueryFilters()
                .AnyAsync(u => u.Email.ToLower() == mail && (excludeUserId == null || u.UserId != excludeUserId)))
        {
            ModelState.AddModelError("Email", "A user with that email already exists.");
            ok = false;
        }
        return ok;
    }

    // GET: Users
    public async Task<IActionResult> Index()
    {
        var users = await _context.Users.ToListAsync();

        // Role list for the inline pill dropdown — what THIS caller can grant.
        // SuperAdmin sees all roles; Admin sees everything except SuperAdmin.
        var grantable = User.IsInRole(Roles.SuperAdmin)
            ? Roles.AssignableToWebUsers
            : Roles.GrantableByAdmin;
        ViewBag.RoleList = new SelectList(grantable);
        ViewBag.GrantableRoles = grantable;
        ViewBag.ActorIsSuperAdmin = User.IsInRole(Roles.SuperAdmin);
        ViewBag.ActorName = User.Identity?.Name ?? "";

        return View(users);
    }

    // GET: Users/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var user = await _context.Users.FindAsync(id);

        if (user == null)
        {
            return NotFound();
        }

        // Last 10 audit events for this user — drives the Recent activity
        // card on the Details view (Phase D of role-access plan).
        ViewBag.RecentEvents = await _context.AuditLogs
            .Where(a => a.EntityType == "User" && a.EntityId == user.UserId)
            .OrderByDescending(a => a.At)
            .Take(10)
            .ToListAsync();

        return View(user);
    }

    // GET: Users/Create
    public IActionResult Create()
    {
        return View();
    }

    // POST: Users/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(User user, string password)
    {
        // SECURITY (audit C5): the User entity was previously model-bound wholesale
        // (no [Bind] allow-list) and added directly, with NO role-grant check on
        // Create — so a non-SuperAdmin Admin could POST Role=SuperAdmin (and an
        // empty CompanyId) to self-escalate to a cross-company super account. We
        // now (a) rebuild the row from allow-listed fields only, (b) decide Role
        // and CompanyId server-side, and (c) run the same ValidateRoleChangeAsync
        // gate that Edit/ChangeRole use.
        if (string.IsNullOrWhiteSpace(password))
            ModelState.AddModelError("password", "Password is required.");
        else
        {
            // SECURITY (audit M3): enforce the shared password policy on create.
            var pwErr = RentCollection.Web.Services.PasswordPolicy.Validate(password);
            if (pwErr != null) ModelState.AddModelError("password", pwErr);
        }

        var requestedRole = string.IsNullOrWhiteSpace(user.Role) ? Roles.Manager : user.Role;
        var actorIsSuper = User.IsInRole(Roles.SuperAdmin);

        int? actorCompanyId = null;
        if (int.TryParse(User.FindFirst("companyId")?.Value, out var cid) && cid > 0)
            actorCompanyId = cid;

        // Enforce grant rules (incl. "only SuperAdmin can grant SuperAdmin" and the
        // assignable-role allow-list). Target starts with no role so this checks the
        // grant, not a mutation.
        var roleErr = await ValidateRoleChangeAsync(
            new User { Username = user.Username, Email = user.Email, Role = string.Empty },
            requestedRole);
        if (roleErr != null)
            ModelState.AddModelError("Role", roleErr);

        // Non-super actors may only create users inside their own company.
        var companyId = actorIsSuper ? (user.CompanyId ?? actorCompanyId) : actorCompanyId;
        if (!actorIsSuper && companyId == null)
            ModelState.AddModelError(string.Empty, "Your account has no company assigned; cannot create users.");

        // Username / Email must be unique (case-insensitive, across companies).
        await ValidateUniqueIdentityAsync(user.Username, user.Email, excludeUserId: null);

        if (ModelState.IsValid)
        {
            var newUser = new User
            {
                Username = user.Username,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                PhoneNumber = user.PhoneNumber,
                Role = requestedRole,
                CompanyId = companyId,
                PasswordHash = _passwordHasher.HashPassword(password),
                CreatedDate = DateTime.UtcNow,
                IsActive = true,
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            // The audit service only Adds the row to the context; the first save
            // was needed to obtain UserId, so save again or the entry is lost.
            await _audit.WriteAsync("User", newUser.UserId, "Created", User.Identity?.Name ?? "system",
                new { newUser.Username, newUser.Email, newUser.Role, newUser.CompanyId });
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "User created successfully.";
            _logger.LogInformation("User {Email} created with role {Role} in company {CompanyId}.",
                newUser.Email, newUser.Role, newUser.CompanyId);

            return RedirectToAction(nameof(Index));
        }

        return View(user);
    }

    // GET: Users/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var user = await _context.Users.FindAsync(id);

        if (user == null)
        {
            return NotFound();
        }

        return View(user);
    }

    // POST: Users/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, User user, string? newPassword)
    {
        if (id != user.UserId)
        {
            return NotFound();
        }

        if (ModelState.IsValid)
        {
            try
            {
                var existingUser = await _context.Users.FindAsync(id);
                if (existingUser == null)
                {
                    return NotFound();
                }

                // SECURITY: a company Admin must never be able to edit a SuperAdmin
                // (reset their password, deactivate them, change their email...).
                if (existingUser.Role == Roles.SuperAdmin && !User.IsInRole(Roles.SuperAdmin))
                {
                    _logger.LogWarning("User {Actor} attempted to edit SuperAdmin account {TargetId}.",
                        User.Identity?.Name, existingUser.UserId);
                    TempData["ErrorMessage"] = "Only a SuperAdmin can edit a SuperAdmin account.";
                    return RedirectToAction(nameof(Index));
                }

                // Username / Email must stay unique (case-insensitive, across companies).
                if (!await ValidateUniqueIdentityAsync(user.Username, user.Email, excludeUserId: id))
                    return View(user);

                // Password resets go through the shared policy, same as Create.
                if (!string.IsNullOrEmpty(newPassword))
                {
                    var pwErr = RentCollection.Web.Services.PasswordPolicy.Validate(newPassword);
                    if (pwErr != null)
                    {
                        ModelState.AddModelError("newPassword", pwErr);
                        return View(user);
                    }
                }

                // Deactivation has the same blast radius as delete — apply the
                // self / last-SuperAdmin / last-admin guards when IsActive flips off.
                if (existingUser.IsActive && !user.IsActive)
                {
                    var deactErr = await ValidateDeactivateAsync(existingUser);
                    if (deactErr != null)
                    {
                        TempData["ErrorMessage"] = deactErr;
                        return RedirectToAction(nameof(Edit), new { id });
                    }
                }

                // Role-change safeguards apply BEFORE we apply any updates.
                // Same rule set as ChangeRole; same audit row when the role
                // actually moves.
                var oldRole = existingUser.Role;
                if (oldRole != user.Role)
                {
                    var err = await ValidateRoleChangeAsync(existingUser, user.Role);
                    if (err != null)
                    {
                        TempData["ErrorMessage"] = err;
                        return RedirectToAction(nameof(Edit), new { id });
                    }
                    await _audit.WriteAsync(
                        entityType: "User",
                        entityId: existingUser.UserId,
                        action: "RoleChanged",
                        byUser: User.Identity?.Name ?? "system",
                        payload: new { from = oldRole, to = user.Role, via = "Edit" });
                }

                // Update properties
                existingUser.Username = user.Username;
                existingUser.Email = user.Email;
                existingUser.FirstName = user.FirstName;
                existingUser.LastName = user.LastName;
                existingUser.PhoneNumber = user.PhoneNumber;
                existingUser.Role = user.Role;
                existingUser.IsActive = user.IsActive;
                existingUser.EmailVerified = user.EmailVerified;
                existingUser.ModifiedDate = DateTime.UtcNow;

                // Update password if provided
                if (!string.IsNullOrEmpty(newPassword))
                {
                    existingUser.PasswordHash = _passwordHasher.HashPassword(newPassword);
                }

                _context.Update(existingUser);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "User updated successfully.";
                _logger.LogInformation($"User {user.Email} updated successfully.");
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!UserExists(user.UserId))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return RedirectToAction(nameof(Index));
        }

        return View(user);
    }

    // GET: Users/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var user = await _context.Users.FindAsync(id);

        if (user == null)
        {
            return NotFound();
        }

        return View(user);
    }

    // POST: Users/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return RedirectToAction(nameof(Index));

        var err = await ValidateDeleteAsync(user);
        if (err != null)
        {
            TempData["ErrorMessage"] = err;
            return RedirectToAction(nameof(Index));
        }

        _context.Users.Remove(user);
        await _audit.WriteAsync(
            entityType: "User",
            entityId: user.UserId,
            action: "Deleted",
            byUser: User.Identity?.Name ?? "system",
            payload: new { username = user.Username, role = user.Role });
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "User deleted successfully.";
        _logger.LogInformation($"User {user.Email} deleted successfully.");

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Read-only permission matrix — rows are role names, columns are
    /// high-level capability groups. Hand-maintained but lives next to the
    /// [Authorize] attributes so drift is easy to spot.
    /// </summary>
    [HttpGet]
    public IActionResult Permissions() => View();

    /// <summary>
    /// Inline role change from the Users list. Same safeguards as Edit,
    /// always writes an AuditLog row. Returns JSON so the inline pill
    /// can update without a full page reload.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeRole(int id, string newRole)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        var oldRole = user.Role;
        if (string.Equals(oldRole, newRole, StringComparison.Ordinal))
            return Json(new { success = true, role = oldRole, message = "No change." });

        var err = await ValidateRoleChangeAsync(user, newRole);
        if (err != null)
            return BadRequest(new { success = false, message = err });

        user.Role = newRole;
        user.ModifiedDate = DateTime.UtcNow;
        await _audit.WriteAsync(
            entityType: "User",
            entityId: user.UserId,
            action: "RoleChanged",
            byUser: User.Identity?.Name ?? "system",
            payload: new { from = oldRole, to = newRole, via = "Inline" });
        await _context.SaveChangesAsync();

        return Json(new { success = true, role = newRole,
            message = $"Role updated from {oldRole} → {newRole}." });
    }

    private bool UserExists(int id)
    {
        return _context.Users.Any(e => e.UserId == id);
    }
}
