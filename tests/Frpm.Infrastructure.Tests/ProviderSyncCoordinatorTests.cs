using Frpm.Infrastructure.Sync;

namespace Frpm.Infrastructure.Tests;

public sealed class ProviderSyncCoordinatorTests
{
    [Fact]
    public async Task RequestSync_WakesCurrentWaitAndUpdatesNextSyncTime()
    {
        using var coordinator = new ProviderSyncCoordinator();
        var wait = coordinator.WaitAsync(TimeSpan.FromMinutes(1), CancellationToken.None);

        coordinator.RequestSync();

        Assert.True(await wait);
        Assert.NotNull(coordinator.Snapshot().NextSyncAt);
    }

    [Fact]
    public void MarkSyncCompleted_RecordsLastAndNextSyncTime()
    {
        using var coordinator = new ProviderSyncCoordinator();
        var completedAt = DateTimeOffset.UtcNow;

        coordinator.MarkSyncCompleted(completedAt, TimeSpan.FromSeconds(45));

        var snapshot = coordinator.Snapshot();
        Assert.Equal(completedAt, snapshot.LastSyncAt);
        Assert.Equal(completedAt.AddSeconds(45), snapshot.NextSyncAt);
    }
}
