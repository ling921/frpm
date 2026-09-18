using Frpm.Abstractions.Services;
using Frpm.Data;
using Frpm.Domain.Entities;
using Frpm.Domain.Enums;
using Frpm.Infrastructure.Extensions;
using Frpm.Infrastructure.Providers;
using Frpm.Infrastructure.Providers.MeFrp;
using Frpm.Infrastructure.Runtime;
using Frpm.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Frpm.Infrastructure.Tests;

public sealed class DependencyRegistrationTests
{
    [Fact]
    public void AutoInject_registers_all_provider_and_remote_service_implementations()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"frpm-di-{Guid.NewGuid():N}");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Frpm:Storage:DataDirectory"] = dataDirectory
                })
                .Build();
            var services = new ServiceCollection();

            services.AddFrpmInfrastructure(configuration);

            var providerDescriptors = services.Where(x => x.ServiceType == typeof(IFrpProvider)).ToList();
            Assert.Equal(3, providerDescriptors.Count);
            Assert.Contains(providerDescriptors, descriptor =>
                descriptor.ServiceKey?.Equals(nameof(Frpm.Domain.Enums.ProviderType.LoliaFrp)) == true &&
                descriptor.KeyedImplementationType == typeof(LoliaFrpProvider));
            Assert.Contains(providerDescriptors, descriptor =>
                descriptor.ServiceKey?.Equals(nameof(Frpm.Domain.Enums.ProviderType.SakuraFrp)) == true &&
                descriptor.KeyedImplementationType == typeof(SakuraFrpProvider));
            Assert.Contains(providerDescriptors, descriptor =>
                descriptor.ServiceKey?.Equals(nameof(Frpm.Domain.Enums.ProviderType.MeFrp)) == true &&
                descriptor.KeyedImplementationType == typeof(MeFrpProvider));
            Assert.Contains(services, x => x.ServiceType == typeof(IProviderAccountService));
            Assert.Contains(services, x => x.ServiceType == typeof(ITunnelService));
            Assert.Contains(services, x => x.ServiceType == typeof(ICliPackageService));
            Assert.Contains(services, x => x.ServiceType == typeof(ILogService));
            Assert.Contains(services, x => x.ServiceType == typeof(IProviderOAuthService));
            Assert.Contains(services, x => x.ServiceType == typeof(ISystemSettingsService));
            Assert.Contains(services, x => x.ServiceType == typeof(IUserPreferencesService));
            Assert.Contains(services, x => x.ServiceType == typeof(RunLogBuffer) && x.Lifetime == ServiceLifetime.Singleton);
            Assert.Contains(services, x => x.ServiceType == typeof(ProviderSyncCoordinator) && x.Lifetime == ServiceLifetime.Singleton);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true);
        }
    }

    [Fact]
    public async Task Mapster_projections_materialize_management_models_with_navigation_fields()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"frpm-mapster-{Guid.NewGuid():N}");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Frpm:Storage:DataDirectory"] = dataDirectory
                })
                .Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddFrpmInfrastructure(configuration);
            await using (var provider = services.BuildServiceProvider())
            {
                var factory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
                await using (var db = await factory.CreateDbContextAsync())
                {
                    await db.Database.EnsureCreatedAsync();
                    var account = new ProviderAccount
                    {
                        ProviderType = ProviderType.SakuraFrp,
                        DisplayName = "测试账号",
                        ApiBaseUrl = "https://example.test",
                        ProtectedCredentials = "protected"
                    };
                    var tunnel = new Tunnel
                    {
                        ProviderAccount = account,
                        RemoteId = "remote-1",
                        Name = "测试隧道",
                        Type = "tcp",
                        NodeId = "5",
                        NodeName = "香港节点"
                    };
                    var package = new CliPackage
                    {
                        ProviderType = ProviderType.SakuraFrp,
                        Version = "1.0.0",
                        OperatingSystem = "windows",
                        Architecture = "x64",
                        Sha256 = new string('a', 64),
                        InstallDirectory = dataDirectory,
                        ExecutablePath = Path.Combine(dataDirectory, "frpc.exe")
                    };
                    db.AddRange(account, tunnel, package);
                    await db.SaveChangesAsync();
                }

                await using var scope = provider.CreateAsyncScope();
                var tunnels = await scope.ServiceProvider.GetRequiredService<ITunnelService>().GetListAsync();
                var packages = await scope.ServiceProvider.GetRequiredService<ICliPackageService>().GetListAsync();

                var tunnelModel = Assert.Single(tunnels);
                Assert.Equal("测试账号", tunnelModel.ProviderAccountName);
                Assert.Equal(ProviderType.SakuraFrp, tunnelModel.ProviderType);
                Assert.Equal("5", tunnelModel.NodeId);
                Assert.Equal("香港节点", tunnelModel.NodeName);
                Assert.Equal("1.0.0", Assert.Single(packages).Version);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true);
        }
    }

    [Fact]
    public async Task System_settings_are_persisted_and_signal_the_sync_coordinator()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"frpm-settings-{Guid.NewGuid():N}");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Frpm:Storage:DataDirectory"] = dataDirectory
                })
                .Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddFrpmInfrastructure(configuration);

            await using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                db.SystemConfigurations.Add(new SystemConfiguration
                {
                    OperatingSystem = "windows",
                    OperatingSystemDescription = "Windows",
                    Architecture = "x64",
                    FrameworkDescription = ".NET test",
                    ApplicationVersion = "1.0.0"
                });
                await db.SaveChangesAsync();
            }

            await using var scope = provider.CreateAsyncScope();
            var settings = await scope.ServiceProvider.GetRequiredService<ISystemSettingsService>()
                .SaveAsync(new(45));

            Assert.Equal(45, settings.ProviderSyncIntervalSeconds);
            Assert.NotNull(provider.GetRequiredService<ProviderSyncCoordinator>().Snapshot().NextSyncAt);
            await using var verificationDb = await factory.CreateDbContextAsync();
            Assert.Equal(45, (await verificationDb.SystemConfigurations.SingleAsync()).ProviderSyncIntervalSeconds);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true);
        }
    }
}
