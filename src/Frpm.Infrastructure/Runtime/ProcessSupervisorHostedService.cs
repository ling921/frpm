using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Frpm.Infrastructure.Runtime;

public sealed class ProcessSupervisorHostedService(
    ITunnelProcessSupervisor supervisor,
    ILogger<ProcessSupervisorHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try { await supervisor.RestoreAsync(cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "恢复隧道进程失败"); }
    }

    public Task StopAsync(CancellationToken cancellationToken) => supervisor.StopAllAsync("应用正在关闭。", true, cancellationToken);
}
