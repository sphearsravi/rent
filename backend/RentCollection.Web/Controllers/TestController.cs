using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.Controllers;

// Diagnostic-only — never expose in production. Routes return 404 outside Development.
#if DEBUG
public class TestController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public TestController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        _context = context;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<IActionResult> DbConnection()
    {
        var result = new
        {
            Status = "Unknown",
            Message = "",
            Details = new Dictionary<string, object>()
        };

        try
        {
            // Test if we can connect to the database
            var canConnect = await _context.Database.CanConnectAsync();

            if (canConnect)
            {
                // Get database provider
                var provider = _context.Database.ProviderName;

                // Get connection string (masked)
                var connectionString = _context.Database.GetConnectionString();
                var maskedConnectionString = MaskPassword(connectionString);

                // Try to get table count
                var tableCount = 0;
                try
                {
                    var tables = await _context.Database.SqlQueryRaw<string>(
                        "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'"
                    ).ToListAsync();
                    tableCount = tables.Count;
                }
                catch (Exception ex)
                {
                    result = new
                    {
                        Status = "Connected but query failed",
                        Message = $"Connected to database but couldn't query tables: {ex.Message}",
                        Details = new Dictionary<string, object>
                        {
                            { "Provider", provider ?? "Unknown" },
                            { "ConnectionString", maskedConnectionString ?? "Not available" },
                            { "Error", ex.Message }
                        }
                    };
                    return View("DbTest", result);
                }

                result = new
                {
                    Status = "Success",
                    Message = "Successfully connected to the database!",
                    Details = new Dictionary<string, object>
                    {
                        { "Provider", provider ?? "Unknown" },
                        { "ConnectionString", maskedConnectionString ?? "Not available" },
                        { "TableCount", tableCount }
                    }
                };
            }
            else
            {
                result = new
                {
                    Status = "Failed",
                    Message = "Cannot connect to the database.",
                    Details = new Dictionary<string, object>
                    {
                        { "Provider", _context.Database.ProviderName ?? "Unknown" }
                    }
                };
            }
        }
        catch (Exception ex)
        {
            result = new
            {
                Status = "Error",
                Message = $"Database connection error: {ex.Message}",
                Details = new Dictionary<string, object>
                {
                    { "ExceptionType", ex.GetType().Name },
                    { "StackTrace", ex.StackTrace ?? "Not available" }
                }
            };
        }

        return View("DbTest", result);
    }

    public async Task<IActionResult> SeedAdmin()
    {
        try
        {
            // Check if admin exists
            var adminEmail = "admin@rentcollection.com";
            var existingAdmin = await _userManager.FindByEmailAsync(adminEmail);

            if (existingAdmin != null)
            {
                var roles = await _userManager.GetRolesAsync(existingAdmin);
                return Content($"Admin user already exists!\n\nEmail: {adminEmail}\nRoles: {string.Join(", ", roles)}\nIsActive: {existingAdmin.IsActive}\n\nYou can login with:\nEmail: {adminEmail}\nPassword: Admin@123");
            }

            // Seed Roles first
            string[] roleNames = { "Admin", "Manager", "User", "Accountant" };
            foreach (var roleName in roleNames)
            {
                if (!await _roleManager.RoleExistsAsync(roleName))
                {
                    await _roleManager.CreateAsync(new IdentityRole(roleName));
                }
            }

            // Create admin user
            var admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FirstName = "Admin",
                LastName = "User",
                EmailConfirmed = true,
                EmailVerified = true,
                IsActive = true,
                CreatedDate = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(admin, "Admin@123");

            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(admin, "Admin");
                return Content($"SUCCESS! Admin user created!\n\nEmail: {adminEmail}\nPassword: Admin@123\n\nYou can now login with these credentials.");
            }
            else
            {
                return Content($"Failed to create admin user:\n\n{string.Join("\n", result.Errors.Select(e => e.Description))}");
            }
        }
        catch (Exception ex)
        {
            return Content($"Error: {ex.Message}\n\nStack Trace: {ex.StackTrace}");
        }
    }

    public async Task<IActionResult> CheckUsers()
    {
        try
        {
            var users = await _userManager.Users.ToListAsync();
            if (!users.Any())
            {
                return Content("No users found in database.\n\nVisit /Test/SeedAdmin to create the admin user.");
            }

            var userInfo = new System.Text.StringBuilder();
            userInfo.AppendLine($"Found {users.Count} user(s) in database:\n");

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                userInfo.AppendLine($"Email: {user.Email}");
                userInfo.AppendLine($"Name: {user.FirstName} {user.LastName}");
                userInfo.AppendLine($"Roles: {string.Join(", ", roles)}");
                userInfo.AppendLine($"IsActive: {user.IsActive}");
                userInfo.AppendLine($"EmailVerified: {user.EmailVerified}");
                userInfo.AppendLine("---");
            }

            return Content(userInfo.ToString());
        }
        catch (Exception ex)
        {
            return Content($"Error: {ex.Message}\n\nStack Trace: {ex.StackTrace}");
        }
    }

    public async Task<IActionResult> SeedCustomAdmin()
    {
        try
        {
            var adminEmail = "admin@rentcollection.com";
            var adminUsername = "admin";

            // Check if admin exists in custom Users table
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == adminEmail || u.Username == adminUsername);

            if (existingUser != null)
            {
                return Content($"Admin user already exists in custom Users table!\n\nUsername: {existingUser.Username}\nEmail: {existingUser.Email}\nRole: {existingUser.Role}\nIsActive: {existingUser.IsActive}\n\nYou can login with:\nUsername: {adminUsername}\nPassword: Admin@123");
            }

            // Hash password using PBKDF2
            var passwordHasher = new RentCollection.Application.Services.PasswordHasher();
            var passwordHash = passwordHasher.HashPassword("Admin@123");

            // Create admin user in custom Users table
            var admin = new User
            {
                Username = adminUsername,
                Email = adminEmail,
                PasswordHash = passwordHash,
                FirstName = "Admin",
                LastName = "User",
                PhoneNumber = null,
                Role = "Admin",
                IsActive = true,
                EmailVerified = true,
                CreatedDate = DateTime.UtcNow
            };

            _context.Users.Add(admin);
            await _context.SaveChangesAsync();

            return Content($"SUCCESS! Admin user created in custom Users table!\n\nUsername: {adminUsername}\nEmail: {adminEmail}\nPassword: Admin@123\nRole: Admin\n\nYou can now login with these credentials.");
        }
        catch (Exception ex)
        {
            return Content($"Error: {ex.Message}\n\nStack Trace: {ex.StackTrace}");
        }
    }

    public async Task<IActionResult> CheckCustomUsers()
    {
        try
        {
            var users = await _context.Users.ToListAsync();
            if (!users.Any())
            {
                return Content("No users found in custom Users table.\n\nVisit /Test/SeedCustomAdmin to create the admin user.");
            }

            var userInfo = new System.Text.StringBuilder();
            userInfo.AppendLine($"Found {users.Count} user(s) in custom Users table:\n");

            foreach (var user in users)
            {
                userInfo.AppendLine($"UserId: {user.UserId}");
                userInfo.AppendLine($"Username: {user.Username}");
                userInfo.AppendLine($"Email: {user.Email}");
                userInfo.AppendLine($"Name: {user.FirstName} {user.LastName}");
                userInfo.AppendLine($"Role: {user.Role}");
                userInfo.AppendLine($"IsActive: {user.IsActive}");
                userInfo.AppendLine($"EmailVerified: {user.EmailVerified}");
                userInfo.AppendLine($"CreatedDate: {user.CreatedDate}");
                userInfo.AppendLine($"LastLogin: {user.LastLogin?.ToString() ?? "Never"}");
                userInfo.AppendLine("---");
            }

            return Content(userInfo.ToString());
        }
        catch (Exception ex)
        {
            return Content($"Error: {ex.Message}\n\nStack Trace: {ex.StackTrace}");
        }
    }

    public async Task<IActionResult> UpdateAdminCredentials()
    {
        try
        {
            // Find the admin user
            var admin = await _context.Users.FirstOrDefaultAsync(u => u.Username == "admin");

            if (admin == null)
            {
                return Content("Admin user not found. Please create one first using /Test/SeedCustomAdmin");
            }

            // Update credentials to simpler ones
            var passwordHasher = new RentCollection.Application.Services.PasswordHasher();

            admin.Username = "admin";
            admin.Email = "admin@test.com";
            admin.PasswordHash = passwordHasher.HashPassword("admin123");
            admin.FirstName = "System";
            admin.LastName = "Administrator";
            admin.IsActive = true;
            admin.EmailVerified = true;
            admin.ModifiedDate = DateTime.UtcNow;

            _context.Users.Update(admin);
            await _context.SaveChangesAsync();

            return Content($"SUCCESS! Admin credentials updated!\n\nNew Login Details:\nUsername: admin\nEmail: admin@test.com\nPassword: admin123\n\nYou can now login with these credentials.");
        }
        catch (Exception ex)
        {
            return Content($"Error: {ex.Message}\n\nStack Trace: {ex.StackTrace}");
        }
    }

    private string? MaskPassword(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return connectionString;

        var parts = connectionString.Split(';');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Trim().StartsWith("Password=", StringComparison.OrdinalIgnoreCase) ||
                parts[i].Trim().StartsWith("Pwd=", StringComparison.OrdinalIgnoreCase))
            {
                parts[i] = parts[i].Split('=')[0] + "=****";
            }
        }
        return string.Join(";", parts);
    }
}
#endif
