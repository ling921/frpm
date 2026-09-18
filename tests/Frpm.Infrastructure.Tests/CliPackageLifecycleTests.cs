using Frpm.Data;
using Frpm.Domain.Entities;
using Frpm.Domain.Enums;
using Frpm.Infrastructure.Options;
using Frpm.Infrastructure.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;
using System.Runtime.InteropServices;

namespace Frpm.Infrastructure.Tests;

public sealed class CliPackageLifecycleTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "frpm-cli-lifecycle-tests", Guid.NewGuid().ToString("N"));
    private readonly string _databasePath;

    public CliPackageLifecycleTests()
    {
        Directory.CreateDirectory(_testDirectory);
        _databasePath = Path.Combine(_testDirectory, "test.db");
    }

    [Fact]
    public async Task InstallSameVersion_RequiresConfirmationAndReplacesExistingRecord()
    {
        var (manager, factory) = await CreateManagerAsync();
        var original = await InstallAsync(manager, "0.71.0", 1, false);
        var originalDirectory = original.InstallDirectory;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InstallAsync(manager, "0.71.0", 2, false));
        Assert.Contains("确认", error.Message);

        var replacement = await InstallAsync(manager, "0.71.0", 3, true);

        Assert.Equal(original.Id, replacement.Id);
        Assert.NotEqual(originalDirectory, replacement.InstallDirectory);
        Assert.False(Directory.Exists(originalDirectory));
        await using var db = await factory.CreateDbContextAsync();
        Assert.Single(await db.CliPackages.ToListAsync());
        Assert.Null((await db.CliPackages.SingleAsync()).DeletedAt);
    }

    [Fact]
    public async Task Delete_PreservesRunHistoryAndAllowsSameVersionToBeInstalledAgain()
    {
        var (manager, factory) = await CreateManagerAsync();
        var package = await InstallAsync(manager, "0.71.0", 1, false);
        var installDirectory = package.InstallDirectory;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var account = new ProviderAccount
            {
                ProviderType = ProviderType.LoliaFrp,
                DisplayName = "test",
                ApiBaseUrl = "https://example.test",
                ProtectedCredentials = "test"
            };
            var tunnel = new Tunnel
            {
                ProviderAccount = account,
                RemoteId = "tunnel-1",
                Name = "test",
                Type = "tcp"
            };
            db.TunnelRuns.Add(new TunnelRun
            {
                Tunnel = tunnel,
                CliPackageId = package.Id,
                ExecutablePath = package.ExecutablePath,
                LogPath = Path.Combine(_testDirectory, "run.log")
            });
            await db.SaveChangesAsync();
        }

        await manager.DeleteAsync(package.Id, CancellationToken.None);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var deleted = await db.CliPackages.SingleAsync();
            Assert.NotNull(deleted.DeletedAt);
            Assert.False(deleted.IsActive);
            Assert.Single(await db.TunnelRuns.ToListAsync());
        }
        Assert.False(Directory.Exists(installDirectory));

        var reinstalled = await InstallAsync(manager, "0.71.0", 2, false);
        Assert.Equal(package.Id, reinstalled.Id);
        Assert.Null(reinstalled.DeletedAt);
    }

    private async Task<(CliPackageManager Manager, TestDbContextFactory Factory)> CreateManagerAsync()
    {
        var factory = new TestDbContextFactory(_databasePath);
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
        }

        var manager = new CliPackageManager(
            factory,
            new UnusedHttpClientFactory(),
            new NoOpSupervisor(),
            Microsoft.Extensions.Options.Options.Create(new FrpmStorageOptions { DataDirectory = Path.Combine(_testDirectory, "data") }));
        return (manager, factory);
    }

    private static async Task<CliPackage> InstallAsync(CliPackageManager manager, string version, byte marker, bool replaceExisting)
    {
        var header = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new byte[] { (byte)'M', (byte)'Z', marker, 0 }
            : new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F', marker };
        await using var stream = new MemoryStream(header);
        var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "frpc.exe" : "frpc";
        return await manager.InstallFromStreamAsync(
            ProviderType.LoliaFrp, version, fileName, stream, null, replaceExisting, CancellationToken.None);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        var resolved = Path.GetFullPath(_testDirectory);
        var expectedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "frpm-cli-lifecycle-tests")) + Path.DirectorySeparatorChar;
        if (resolved.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved))
        {
            Directory.Delete(resolved, true);
        }
    }

    private sealed class TestDbContextFactory(string databasePath) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={databasePath}").Options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class NoOpSupervisor : ITunnelProcessSupervisor
    {
        public Task StartAsync(Guid tunnelId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(Guid tunnelId, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAllAsync(string reason, bool preserveDesiredState = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
