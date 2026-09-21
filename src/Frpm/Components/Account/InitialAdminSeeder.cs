using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace Frpm.Components.Account;

internal static class InitialAdminSeeder
{
    public const string UserName = "admin";
    public const string DefaultPassword = "Frpm@123";

    public static async Task SeedAsync(IServiceProvider services, ILogger logger)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        if (await userManager.Users.AnyAsync())
        {
            return;
        }

        var user = new ApplicationUser
        {
            UserName = UserName,
            Email = "admin@frpm.local",
            MustChangePassword = true,
            EmailConfirmed = true,
            LockoutEnabled = false
        };

        var result = await userManager.CreateAsync(user, DefaultPassword);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(error => error.Description));
            throw new InvalidOperationException($"Unable to create the initial administrator: {errors}");
        }

        logger.LogWarning(
            "No users were found. Initial administrator created. Username: {UserName}; Password: {Password}. " +
            "The password must be changed at the first login.",
            UserName,
            DefaultPassword);
    }

    public static async Task<string> ResetPasswordAsync(IServiceProvider services, ILogger logger)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(UserName);
        if (user is null)
        {
            throw new InvalidOperationException($"Administrator account '{UserName}' was not found.");
        }

        var temporaryPassword = $"Frpm!{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}a";
        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var resetResult = await userManager.ResetPasswordAsync(user, resetToken, temporaryPassword);
        if (!resetResult.Succeeded)
        {
            var errors = string.Join("; ", resetResult.Errors.Select(error => error.Description));
            throw new InvalidOperationException($"Unable to reset the administrator password: {errors}");
        }

        await userManager.SetLockoutEndDateAsync(user, null);
        user.MustChangePassword = true;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            var errors = string.Join("; ", updateResult.Errors.Select(error => error.Description));
            throw new InvalidOperationException($"Unable to require an administrator password change: {errors}");
        }

        logger.LogWarning("The initial administrator password was reset. A password change is required at the next login.");
        return temporaryPassword;
    }
}
