using Frpm.Data;
using Frpm.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Frpm.Infrastructure.Sync;

public sealed class TunnelSyncWorker(
    IServiceScopeFactory scopeFactory,
    ProviderSyncCoordinator coordinator,
    ILogger<TunnelSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await coordinator.WaitAsync(TimeSpan.FromSeconds(10), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromSeconds(60);
            try
            {
                using var scope = scopeFactory.CreateScope();
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
                await using var db = await factory.CreateDbContextAsync(stoppingToken);
                var intervalSeconds = await db.SystemConfigurations
                    .Where(item => item.Id == SystemConfiguration.SingletonId)
                    .Select(item => item.ProviderSyncIntervalSeconds)
                    .SingleAsync(stoppingToken);
                interval = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 15, 3_600));
                var ids = await db.ProviderAccounts.Where(x => x.Enabled).Select(x => x.Id).ToListAsync(stoppingToken);
                var sync = scope.ServiceProvider.GetRequiredService<TunnelSyncService>();
                foreach (var id in ids)
                {
                    try { await sync.SyncAsync(id, stoppingToken); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "后台同步 {AccountId} 失败", id); }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "后台隧道同步任务失败");
            }
            finally
            {
                coordinator.MarkSyncCompleted(DateTimeOffset.UtcNow, interval);
            }

            await coordinator.WaitAsync(interval, stoppingToken);
        }
    }
}
