namespace Frpm.Infrastructure.Sync;

/// <summary>
/// 保存进程内的同步调度状态，并允许设置变更或人工操作立即唤醒后台同步任务。
/// </summary>
[SingletonService]
public sealed class ProviderSyncCoordinator : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly Lock _gate = new();
    private DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset? _lastSyncAt;
    private DateTimeOffset? _nextSyncAt;

    public void Initialize(DateTimeOffset startedAt)
    {
        lock (_gate) _startedAt = startedAt;
    }

    public void RequestSync()
    {
        lock (_gate)
        {
            _nextSyncAt = DateTimeOffset.UtcNow;
            if (_signal.CurrentCount == 0) _signal.Release();
        }
    }

    public void MarkSyncCompleted(DateTimeOffset completedAt, TimeSpan nextInterval)
    {
        lock (_gate)
        {
            _lastSyncAt = completedAt;
            _nextSyncAt = completedAt + nextInterval;
        }
    }

    public ProviderSyncRuntimeSnapshot Snapshot()
    {
        lock (_gate) return new(_startedAt, _lastSyncAt, _nextSyncAt);
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _signal.WaitAsync(timeout, cancellationToken);

    public void Dispose() => _signal.Dispose();
}

/// <summary>供应商同步后台任务的进程内状态快照。</summary>
public readonly record struct ProviderSyncRuntimeSnapshot(
    DateTimeOffset StartedAt,
    DateTimeOffset? LastSyncAt,
    DateTimeOffset? NextSyncAt);
