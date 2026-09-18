using Frpm.Data;
using Frpm.Domain.Entities;
using Frpm.Infrastructure.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Frpm.Infrastructure.Sync;

/// <summary>
/// 在应用启动时采集并持久化当前运行环境。
/// </summary>
public sealed class SystemRuntimeHostedService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ProviderSyncCoordinator coordinator) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var configuration = await db.SystemConfigurations.SingleOrDefaultAsync(
            item => item.Id == SystemConfiguration.SingletonId,
            cancellationToken);
        if (configuration is null)
        {
            configuration = new SystemConfiguration
            {
                Id = SystemConfiguration.SingletonId,
                FirstStartedAt = now
            };
            db.SystemConfigurations.Add(configuration);
        }

        configuration.LastStartedAt = now;
        configuration.OperatingSystem = TunnelProcessSupervisor.CurrentOs();
        configuration.OperatingSystemDescription = RuntimeInformation.OSDescription;
        configuration.Architecture = TunnelProcessSupervisor.CurrentArchitecture();
        configuration.FrameworkDescription = RuntimeInformation.FrameworkDescription;
        configuration.ApplicationVersion = GetApplicationVersion();
        await db.SaveChangesAsync(cancellationToken);
        coordinator.Initialize(now);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string GetApplicationVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(SystemRuntimeHostedService).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "未知";
        return version.Length <= 100 ? version : version[..100];
    }
}
