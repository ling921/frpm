using Frpm.Abstractions.Models;
using Frpm.Abstractions.Services;
using Frpm.Data;
using Frpm.Domain.Entities;
using Frpm.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;

namespace Frpm.Infrastructure.Services;

[ScopedService(typeof(ISystemSettingsService))]
public sealed class SystemSettingsService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ProviderSyncCoordinator coordinator) : ISystemSettingsService
{
    public async Task<SystemSettingsModel> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var configuration = await GetConfigurationAsync(db, cancellationToken);
        return Map(configuration, coordinator.Snapshot());
    }

    public async Task<SystemSettingsModel> SaveAsync(
        SaveSystemSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ProviderSyncIntervalSeconds is < 15 or > 3_600)
            throw new InvalidOperationException("供应商同步间隔必须在 15–3600 秒之间。");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var configuration = await GetConfigurationAsync(db, cancellationToken);
        configuration.ProviderSyncIntervalSeconds = request.ProviderSyncIntervalSeconds;
        await db.SaveChangesAsync(cancellationToken);
        coordinator.RequestSync();
        return Map(configuration, coordinator.Snapshot());
    }

    public Task RequestProviderSyncAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        coordinator.RequestSync();
        return Task.CompletedTask;
    }

    private static async Task<SystemConfiguration> GetConfigurationAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken) =>
        await db.SystemConfigurations.SingleAsync(
            item => item.Id == SystemConfiguration.SingletonId,
            cancellationToken);

    private static SystemSettingsModel Map(
        SystemConfiguration configuration,
        ProviderSyncRuntimeSnapshot runtime)
    {
        var uptime = Math.Max(0, (long)(DateTimeOffset.UtcNow - runtime.StartedAt).TotalSeconds);
        return new(
            configuration.ProviderSyncIntervalSeconds,
            configuration.InstanceId,
            configuration.OperatingSystem,
            configuration.OperatingSystemDescription,
            configuration.Architecture,
            configuration.FrameworkDescription,
            configuration.ApplicationVersion,
            configuration.FirstStartedAt,
            configuration.LastStartedAt,
            uptime,
            runtime.LastSyncAt,
            runtime.NextSyncAt);
    }
}
