using Microsoft.EntityFrameworkCore;

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
            EmailConfirmed = true
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
}
