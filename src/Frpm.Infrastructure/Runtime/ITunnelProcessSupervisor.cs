namespace Frpm.Infrastructure.Runtime;

public interface ITunnelProcessSupervisor
{
    Task StartAsync(Guid tunnelId, CancellationToken cancellationToken = default);
    Task StopAsync(Guid tunnelId, string reason, CancellationToken cancellationToken = default);
    Task StopAllAsync(string reason, bool preserveDesiredState = false, CancellationToken cancellationToken = default);
    Task RestoreAsync(CancellationToken cancellationToken = default);
}
