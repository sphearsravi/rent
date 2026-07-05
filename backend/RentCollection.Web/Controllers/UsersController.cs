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

    private async Task<string?> ValidateDeleteAsync(User target)
    {
        var actorName = User.Identity?.Name;
        if (string.Equals(actorName, target.Username, StringComparison.OrdinalIgnoreCase)
            || string.Equals(actorName, target.Email, StringComparison.OrdinalIgnoreCase))
            return "You cannot delete your own account.";

        if (target.Role == Roles.SuperAdmin)
        {
            var superAdminCount = await _context.Users
                .CountAsync(u => u.Role == Roles.SuperAdmin && u.IsActive);
            if (superAdminCount <= 1)
                return "Cannot delete the last active SuperAdmin.";
        }

        // Same admin-tier floor as ValidateRoleChangeAsync — never end up
        // with zero usable admin accounts.
        if (target.Role == Roles.SuperAdmin || target.Role == Roles.Admin)
        {
            var adminTierCount = await _context.Users
                .CountAsync(u => (u.Role == Roles.SuperAdmin || u.Role == Roles.Admin) && u.IsActive);
            if (adminTierCount <= 1)
                return "Cannot delete the last active admin — at least one Admin or SuperAdmin must remain.";
        }
        return null;
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
        if (ModelState.IsValid)
        {
            // Hash the password
            user.PasswordHash = _passwordHasher.HashPassword(password);
            user.CreatedDate = DateTime.UtcNow;
            user.IsActive = true;

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "User created successfully.";
            _logger.LogInformation($"User {user.Email} created successfully.");

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
