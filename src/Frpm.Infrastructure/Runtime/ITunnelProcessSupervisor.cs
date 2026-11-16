namespace Frpm.Infrastructure.Runtime;

public interface ITunnelProcessSupervisor
{
    Task StartAsync(Guid tunnelId, CancellationToken cancellationToken = default);
    Task StopAsync(Guid tunnelId, string reason, CancellationToken cancellationToken = default);
    Task StopAllAsync(string reason, bool preserveDesiredState = false, CancellationToken cancellationToken = default);
    Task RestoreAsync(CancellationToken cancellationToken = default);
    Task TryAutoRecoverAsync(Guid tunnelId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task CheckAndRecoverTunnelsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    TunnelCircuitBreakerState? GetCircuitBreakerStatus(Guid tunnelId) => null;
    void ResetCircuitBreaker(Guid tunnelId) { }
}

public sealed class TunnelCircuitBreakerState
{
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? LastFailureTime { get; set; }
    public DateTimeOffset? NextRetryTime { get; set; }
    public bool IsTripped { get; set; }
    public string? TripReason { get; set; }
    public bool RequiresRemoteKick { get; set; }
}
