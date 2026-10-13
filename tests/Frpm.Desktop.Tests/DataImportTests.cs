using Frpm.Application;
using Frpm.Data;
using Frpm.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Frpm.Desktop.Tests;

public sealed class DataImportTests
{
    [Fact]
    public async Task Import_preserves_credentials_and_rebases_cli_and_log_paths()
    {
        await using var source = new TestRuntime();
        await source.StartAsync(seed: true);
        await source.Host.StopAsync();
        var destination = Path.Combine(Path.GetTempPath(), $"frpm-desktop-test-{Guid.NewGuid():N}");
        await FrpmDataImporter.ImportAsync(source.Directory, destination);
        await using var imported = new TestRuntime(destination);
        await imported.StartAsync();
        var factory = imported.Host.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var account = await db.ProviderAccounts.SingleAsync();
        var protector = imported.Host.Services.GetRequiredService<CredentialProtector>();
        Assert.Equal("test-token", protector.Unprotect(account.ProtectedCredentials).AccessToken);
        var cli = await db.CliPackages.SingleAsync();
        Assert.Equal(destination, cli.InstallDirectory);
        Assert.Equal(Path.Combine(destination, "frpc"), cli.ExecutablePath);
        Assert.All(await db.TunnelRuns.ToListAsync(), run => Assert.Equal(Path.Combine(destination, "test.log"), run.LogPath));
        Assert.Equal(2, (await imported.Workspace.GetTunnelsAsync()).Count);
        var sourceFactory = source.Host.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var oldDb = await sourceFactory.CreateDbContextAsync();
        Assert.Equal(source.Directory, (await oldDb.CliPackages.SingleAsync()).InstallDirectory);
    }

    [Fact]
    public async Task Import_refuses_to_overwrite_an_existing_desktop_profile()
    {
        await using var source = new TestRuntime();
        await source.StartAsync();
        await source.Host.StopAsync();
        await using var destination = new TestRuntime();
        await destination.StartAsync();
        await destination.Workspace.SavePreferencesAsync(new("dark", "#5468ff", "#52606d", "#8b9cff", "#a8b3cf"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => FrpmDataImporter.ImportAsync(source.Directory, destination.Directory));
        Assert.Equal("dark", (await destination.Workspace.GetPreferencesAsync()).ThemeMode);
    }
}
