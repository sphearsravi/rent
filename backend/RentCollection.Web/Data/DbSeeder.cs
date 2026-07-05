using Microsoft.AspNetCore.Identity;
using RentCollection.Core.Entities;

namespace RentCollection.Web.Data;

public static class DbSeeder
{
    public static async Task SeedRolesAndAdminAsync(IServiceProvider serviceProvider)
    {
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // Seed Roles
        string[] roleNames = { "Admin", "Manager", "User", "Accountant" };

        foreach (var roleName in roleNames)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole(roleName));
            }
        }

        // Seed Admin User
        var adminEmail = "admin@rentcollection.com";
        var adminUser = await userManager.FindByEmailAsync(adminEmail);

        if (adminUser == null)
        {
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

            var result = await userManager.CreateAsync(admin, "Admin@123");

            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, "Admin");
            }
        }
    }
}
