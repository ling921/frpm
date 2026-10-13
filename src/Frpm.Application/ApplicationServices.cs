using Frpm.Data;
using Frpm.Abstractions.Services;
using Frpm.Infrastructure.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;

namespace Frpm.Application;

public static class ApplicationServices
{
    public static IServiceCollection AddFrpmApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddFrpmInfrastructure(configuration);
        // Use the same Identity persistence schema as the Docker/Web host, without its login UI.
        services.Configure<IdentityOptions>(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version3);
        services.AddSingleton<IProviderOAuthCallback>(provider =>
            (IProviderOAuthCallback)provider.GetRequiredService<IProviderOAuthService>());
        services.AddSingleton<FrpmWorkspace>();
        services.AddSingleton<DesktopOAuthFlow>();
        return services;
    }

    public static async Task InitializeFrpmDatabaseAsync(
        this IServiceProvider services, string? localUserId = null, CancellationToken cancellationToken = default)
    {
        var factory = services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
        if (localUserId is not null && !await db.Users.AnyAsync(x => x.Id == localUserId, cancellationToken))
        {
            // Desktop access is granted by the OS user session; there is no Web login/password.
            db.Users.Add(new ApplicationUser
            {
                Id = localUserId, UserName = "desktop", NormalizedUserName = "DESKTOP",
                Email = "desktop@frpm.local", NormalizedEmail = "DESKTOP@FRPM.LOCAL"
            });
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
