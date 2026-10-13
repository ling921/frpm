using Frpm.Abstractions.Services;
using Frpm.Application;
using Frpm.Data;
using Frpm.Domain.Entities;
using Frpm.Domain.Enums;
using Frpm.Infrastructure.Runtime;
using Frpm.Infrastructure.Security;
using Frpm.Infrastructure.Providers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Frpm.Desktop.Tests;

internal sealed class TestRuntime : IAsyncDisposable
{
    public string Directory { get; }
    public IHost Host { get; }
    public FakeSupervisor Supervisor { get; } = new();
    public FrpmWorkspace Workspace => Host.Services.GetRequiredService<FrpmWorkspace>();

    public TestRuntime(string? directory = null)
    {
        Directory = directory ?? Path.Combine(Path.GetTempPath(), $"frpm-desktop-test-{Guid.NewGuid():N}");
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Frpm:Storage:DataDirectory"] = Directory,
            ["ConnectionStrings:DefaultConnection"] = new SqliteConnectionStringBuilder { DataSource = Path.Combine(Directory, "frpm.db") }.ToString()
        });
        builder.Services.AddFrpmApplication(builder.Configuration);
        builder.Services.AddSingleton<ICurrentUserIdentity, TestUserIdentity>();
        builder.Services.AddSingleton<ITunnelProcessSupervisor>(Supervisor);
        Host = builder.Build();
    }

    public async Task StartAsync(bool seed = false)
    {
        await Host.Services.InitializeFrpmDatabaseAsync("test-local-user");
        if (seed)
        {
            var factory = Host.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            var protector = Host.Services.GetRequiredService<CredentialProtector>();
            await using var db = await factory.CreateDbContextAsync();
            var account = new ProviderAccount
            {
                ProviderType = ProviderType.SakuraFrp, DisplayName = "我的 SakuraFrp", ApiBaseUrl = "https://api.natfrp.com/v4",
                Enabled = false, ProtectedCredentials = protector.Protect(new ProviderCredentials("test-token", null, null, null)),
                LastSyncAt = DateTimeOffset.UtcNow, LastSyncSucceeded = true, LastSyncMessage = "同步成功"
            };
            var package = new CliPackage
            {
                ProviderType = ProviderType.SakuraFrp, Version = "0.64.0", OperatingSystem = TunnelProcessSupervisor.CurrentOs(),
                Architecture = TunnelProcessSupervisor.CurrentArchitecture(), Sha256 = new string('a', 64),
                InstallDirectory = Directory, ExecutablePath = Path.Combine(Directory, "frpc"), IsActive = true
            };
            db.AddRange(account, package);
            foreach (var sample in new[] { ("开发站点", "http", 3000, "dev.example.com"), ("SSH", "tcp", 22, "23322") })
            {
                var tunnel = new Tunnel
                {
                    ProviderAccount = account, RemoteId = Guid.NewGuid().ToString("N"), Name = sample.Item1,
                    Type = sample.Item2, LocalAddress = "127.0.0.1", LocalPort = sample.Item3, RemoteAddress = sample.Item4,
                    NodeId = "1", NodeName = "香港节点", RuntimeState = TunnelRuntimeState.Stopped, RemoteState = TunnelRemoteState.Active
                };
                db.Add(tunnel);
                db.Add(new TunnelRun
                {
                    Tunnel = tunnel, CliPackage = package, ProcessId = 12345, ExecutablePath = package.ExecutablePath,
                    LogPath = Path.Combine(Directory, "test.log"), EndedAt = DateTimeOffset.UtcNow, Summary = "用户已停止隧道。", ExitCode = 0
                });
            }
            await db.SaveChangesAsync();
        }
        await Host.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Host.StopAsync();
        Host.Dispose();
        SqliteConnection.ClearAllPools();
        if (Path.GetDirectoryName(Path.GetFullPath(Directory)) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)
            && System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
    }

    private sealed class TestUserIdentity : ICurrentUserIdentity
    {
        public Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("test-local-user");
    }
}

internal sealed class FakeSupervisor : ITunnelProcessSupervisor
{
    public int Restores { get; private set; }
    public int Shutdowns { get; private set; }
    public int Starts { get; private set; }
    public int Stops { get; private set; }
    public bool PreservedDesiredState { get; private set; }
    public Task StartAsync(Guid id, CancellationToken ct = default) { Starts++; return Task.CompletedTask; }
    public Task StopAsync(Guid id, string reason, CancellationToken ct = default) { Stops++; return Task.CompletedTask; }
    public Task StopAllAsync(string reason, bool preserveDesiredState = false, CancellationToken ct = default)
    {
        Shutdowns++;
        PreservedDesiredState = preserveDesiredState;
        return Task.CompletedTask;
    }
    public Task RestoreAsync(CancellationToken ct = default) { Restores++; return Task.CompletedTask; }
}
