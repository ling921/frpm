using Frpm.Abstractions.Models;
using Frpm.Data;
using Frpm.Domain.Entities;
using Frpm.Domain.Enums;
using Frpm.Infrastructure.Concurrency;
using Frpm.Infrastructure.Providers;
using Frpm.Infrastructure.Runtime;
using Frpm.Infrastructure.Security;
using Frpm.Infrastructure.Services;
using MapsterMapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace Frpm.Infrastructure.Tests;

public sealed class TunnelServiceTests
{
    [Theory]
    [InlineData(TunnelRemoteState.RemoteMissing, 0)]
    [InlineData(TunnelRemoteState.Active, 1)]
    [InlineData(TunnelRemoteState.Inactive, 1)]
    [InlineData(TunnelRemoteState.Unknown, 1)]
    public async Task DeleteAsync_OnlyDeletesRemotelyWhenTunnelIsNotMissing(TunnelRemoteState remoteState, int expectedRequests)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var factory = new TestDbContextFactory(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        var protector = new CredentialProtector(new EphemeralDataProtectionProvider());
        var tunnel = new Tunnel
        {
            Name = "test",
            Type = "tcp",
            RemoteId = "123",
            RemoteState = remoteState,
            ProviderAccount = new ProviderAccount
            {
                ProviderType = ProviderType.SakuraFrp,
                DisplayName = "test",
                ApiBaseUrl = "https://example.test/v4/",
                ProtectedCredentials = remoteState == TunnelRemoteState.RemoteMissing
                    ? "unavailable credentials"
                    : protector.Protect(new ProviderCredentials("token"))
            }
        };
        await using (var db = factory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
            db.Tunnels.Add(tunnel);
            await db.SaveChangesAsync();
        }

        var httpFactory = new RecordingHttpClientFactory();
        var supervisor = new RecordingSupervisor();
        var service = new TunnelService(factory, new TestProviderFactory(new SakuraFrpProvider(httpFactory)),
            protector, supervisor, new OperationLock(), new Mapper(), NullLogger<TunnelService>.Instance);

        await service.DeleteAsync(tunnel.Id);

        Assert.Equal(expectedRequests, httpFactory.Requests);
        Assert.Equal(tunnel.Id, Assert.Single(supervisor.StoppedTunnels));
        await using var verificationDb = factory.CreateDbContext();
        var deleted = await verificationDb.Tunnels.SingleAsync();
        Assert.Equal(TunnelRemoteState.Deleted, deleted.RemoteState);
        Assert.NotNull(deleted.DeletedAt);
        Assert.True(deleted.UpdatedAt >= tunnel.UpdatedAt);
    }

    [Fact]
    public async Task SaveAsync_WhenCreatingTcpUdpTunnel_SavesBothTcpAndUdpEntities()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var factory = new TestDbContextFactory(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        var protector = new CredentialProtector(new EphemeralDataProtectionProvider());
        var account = new ProviderAccount
        {
            ProviderType = ProviderType.LoliaFrp,
            DisplayName = "Lolia Account",
            ApiBaseUrl = "https://api.lolia.link/api/v1/",
            ProtectedCredentials = protector.Protect(new ProviderCredentials("token"))
        };
        await using (var db = factory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
            db.ProviderAccounts.Add(account);
            await db.SaveChangesAsync();
        }

        var supervisor = new RecordingSupervisor();
        var dualProvider = new DualProtocolFakeProvider();
        var service = new TunnelService(factory, new TestProviderFactory(dualProvider),
            protector, supervisor, new OperationLock(), new Mapper(), NullLogger<TunnelService>.Instance);

        var request = new SaveTunnelRequest
        {
            ProviderAccountId = account.Id,
            NodeId = "74",
            Type = "tcp+udp",
            LocalAddress = "127.0.0.1",
            LocalPort = 8080,
            Name = "game-server",
            Remark = "game-server"
        };

        var result = await service.SaveAsync(request);

        Assert.Equal("game-server (TCP)", result.Name);
        Assert.Equal("tcp", result.Type);

        await using var verifyDb = factory.CreateDbContext();
        var tunnels = await verifyDb.Tunnels.OrderBy(t => t.Type).ToListAsync();
        Assert.Equal(2, tunnels.Count);
        Assert.Equal("tcp", tunnels[0].Type);
        Assert.Equal("game-server (TCP)", tunnels[0].Name);
        Assert.Equal("22413", tunnels[0].RemoteAddress);

        Assert.Equal("udp", tunnels[1].Type);
        Assert.Equal("game-server (UDP)", tunnels[1].Name);
        Assert.Equal("22413", tunnels[1].RemoteAddress);
    }

    [Fact]
    public async Task DeleteAsync_StillMarksDeletedWhenRemoteDeleteThrows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var factory = new TestDbContextFactory(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        var protector = new CredentialProtector(new EphemeralDataProtectionProvider());
        var tunnel = new Tunnel
        {
            Name = "remote-error-tunnel",
            Type = "tcp",
            RemoteId = "404",
            RemoteState = TunnelRemoteState.Active,
            ProviderAccount = new ProviderAccount
            {
                ProviderType = ProviderType.SakuraFrp,
                DisplayName = "test",
                ApiBaseUrl = "https://example.test/v4/",
                ProtectedCredentials = protector.Protect(new ProviderCredentials("token"))
            }
        };
        await using (var db = factory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
            db.Tunnels.Add(tunnel);
            await db.SaveChangesAsync();
        }

        var supervisor = new RecordingSupervisor();
        var failingProvider = new FailingDeleteProvider();
        var service = new TunnelService(factory, new TestProviderFactory(failingProvider),
            protector, supervisor, new OperationLock(), new Mapper(), NullLogger<TunnelService>.Instance);

        await service.DeleteAsync(tunnel.Id);

        Assert.Equal(tunnel.Id, Assert.Single(supervisor.StoppedTunnels));
        await using var verificationDb = factory.CreateDbContext();
        var deleted = await verificationDb.Tunnels.SingleAsync();
        Assert.Equal(TunnelRemoteState.Deleted, deleted.RemoteState);
        Assert.NotNull(deleted.DeletedAt);
    }

    [Fact]
    public async Task DeleteAsync_WhenTunnelAlreadyDeleted_CompletesSilentlyWithoutThrowing()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var factory = new TestDbContextFactory(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        var protector = new CredentialProtector(new EphemeralDataProtectionProvider());
        var tunnel = new Tunnel
        {
            Name = "already-deleted",
            Type = "tcp",
            RemoteId = "404",
            RemoteState = TunnelRemoteState.Deleted,
            DeletedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ProviderAccount = new ProviderAccount
            {
                ProviderType = ProviderType.SakuraFrp,
                DisplayName = "test",
                ApiBaseUrl = "https://example.test/v4/",
                ProtectedCredentials = protector.Protect(new ProviderCredentials("token"))
            }
        };
        await using (var db = factory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
            db.Tunnels.Add(tunnel);
            await db.SaveChangesAsync();
        }

        var supervisor = new RecordingSupervisor();
        var service = new TunnelService(factory, new TestProviderFactory(new FailingDeleteProvider()),
            protector, supervisor, new OperationLock(), new Mapper(), NullLogger<TunnelService>.Instance);

        // Deleting already deleted tunnel should not throw
        await service.DeleteAsync(tunnel.Id);
    }

    [Fact]
    public async Task DeleteAsync_WhenSupervisorThrows_StillCleansUpLocalRecord()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var factory = new TestDbContextFactory(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        var protector = new CredentialProtector(new EphemeralDataProtectionProvider());
        var tunnel = new Tunnel
        {
            Name = "supervisor-fail-tunnel",
            Type = "tcp",
            RemoteId = "999",
            RemoteState = TunnelRemoteState.RemoteMissing,
            ProviderAccount = new ProviderAccount
            {
                ProviderType = ProviderType.SakuraFrp,
                DisplayName = "test",
                ApiBaseUrl = "https://example.test/v4/",
                ProtectedCredentials = protector.Protect(new ProviderCredentials("token"))
            }
        };
        await using (var db = factory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
            db.Tunnels.Add(tunnel);
            await db.SaveChangesAsync();
        }

        var supervisor = new FailingSupervisor();
        var service = new TunnelService(factory, new TestProviderFactory(new FailingDeleteProvider()),
            protector, supervisor, new OperationLock(), new Mapper(), NullLogger<TunnelService>.Instance);

        await service.DeleteAsync(tunnel.Id);

        await using var verificationDb = factory.CreateDbContext();
        var deleted = await verificationDb.Tunnels.SingleAsync();
        Assert.Equal(TunnelRemoteState.Deleted, deleted.RemoteState);
        Assert.NotNull(deleted.DeletedAt);
    }

    private sealed class FailingSupervisor : ITunnelProcessSupervisor
    {
        public Task StopAsync(Guid tunnelId, string reason, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Process termination failed");
        public Task StartAsync(Guid tunnelId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopAllAsync(string reason, bool preserveDesiredState = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RestoreAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FailingDeleteProvider : IFrpProvider
    {
        public ProviderType Type => ProviderType.SakuraFrp;
        public ProviderCapabilitiesModel Capabilities => BuiltInProviderCatalog.Get(Type).Capabilities;
        public Task ValidateAccountAsync(string apiBaseUrl, ProviderCredentials credentials, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<ProviderNodeModel>> GetNodesAsync(string apiBaseUrl, ProviderCredentials credentials, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderNodeModel>>([]);
        public Task<ProviderTunnelPage> GetTunnelsAsync(string apiBaseUrl, ProviderCredentials credentials, int page, CancellationToken cancellationToken) => Task.FromResult(new ProviderTunnelPage([], 1, 1));
        public Task<ProviderTunnel> CreateTunnelAsync(string apiBaseUrl, ProviderCredentials credentials, SaveTunnelRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProviderTunnel> UpdateTunnelAsync(string apiBaseUrl, ProviderCredentials credentials, ProviderTunnel current, SaveTunnelRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteTunnelAsync(string apiBaseUrl, ProviderCredentials credentials, ProviderTunnel tunnel, CancellationToken cancellationToken) => throw new InvalidOperationException("404 Not Found");
        public Task<LaunchSpecification> PrepareLaunchAsync(string apiBaseUrl, ProviderCredentials credentials, ProviderTunnel tunnel, string executablePath, string workingDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class DualProtocolFakeProvider : IFrpProvider
    {
        public ProviderType Type => ProviderType.LoliaFrp;
        public ProviderCapabilitiesModel Capabilities => BuiltInProviderCatalog.Get(Type).Capabilities;
        public Task ValidateAccountAsync(string apiBaseUrl, ProviderCredentials credentials, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<ProviderNodeModel>> GetNodesAsync(string apiBaseUrl, ProviderCredentials credentials, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderNodeModel>>([new("74", "Node-74", "node74.lolia.link", true, "HK", "100 Mbps", "tcp / udp", "10000-30000", null)]);
        public Task<ProviderTunnelPage> GetTunnelsAsync(string apiBaseUrl, ProviderCredentials credentials, int page, CancellationToken cancellationToken) => Task.FromResult(new ProviderTunnelPage([], 1, 1));
        public Task<ProviderTunnel> CreateTunnelAsync(string apiBaseUrl, ProviderCredentials credentials, SaveTunnelRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProviderTunnel>> CreateTunnelsAsync(string apiBaseUrl, ProviderCredentials credentials, SaveTunnelRequest request, CancellationToken cancellationToken)
        {
            var tcp = new ProviderTunnel("37757", "game-server (TCP)", "game-server (TCP)", "tcp", "74", "Node-74", "127.0.0.1", 8080, "22413", "inactive", "{\"id\":37757,\"name\":\"831b2942c1964ac4884758969bed3b98\"}");
            var udp = new ProviderTunnel("37758", "game-server (UDP)", "game-server (UDP)", "udp", "74", "Node-74", "127.0.0.1", 8080, "22413", "inactive", "{\"id\":37758,\"name\":\"e04f3ec06c4f401bb2132de08becf73e\"}");
            return Task.FromResult<IReadOnlyList<ProviderTunnel>>([tcp, udp]);
        }
        public Task<ProviderTunnel> UpdateTunnelAsync(string apiBaseUrl, ProviderCredentials credentials, ProviderTunnel current, SaveTunnelRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteTunnelAsync(string apiBaseUrl, ProviderCredentials credentials, ProviderTunnel tunnel, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<LaunchSpecification> PrepareLaunchAsync(string apiBaseUrl, ProviderCredentials credentials, ProviderTunnel tunnel, string executablePath, string workingDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private sealed class TestProviderFactory(IFrpProvider provider) : IFrpProviderFactory
    {
        public IFrpProvider Get(ProviderType type) => provider;
    }

    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        public int Requests { get; private set; }

        public HttpClient CreateClient(string name) => new(new RecordingHandler(this));

        private sealed class RecordingHandler(RecordingHttpClientFactory owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                owner.Requests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
            }
        }
    }

    private sealed class RecordingSupervisor : ITunnelProcessSupervisor
    {
        public List<Guid> StoppedTunnels { get; } = [];

        public Task StopAsync(Guid tunnelId, string reason, CancellationToken cancellationToken = default)
        {
            StoppedTunnels.Add(tunnelId);
            return Task.CompletedTask;
        }

        public Task StartAsync(Guid tunnelId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopAllAsync(string reason, bool preserveDesiredState = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RestoreAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
