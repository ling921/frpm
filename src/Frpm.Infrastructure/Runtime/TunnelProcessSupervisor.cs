using Frpm.Data;
using Frpm.Domain.Entities;
using Frpm.Domain.Enums;
using Frpm.Infrastructure.Concurrency;
using Frpm.Infrastructure.Options;
using Frpm.Infrastructure.Providers;
using Frpm.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;

namespace Frpm.Infrastructure.Runtime;

[SingletonService(typeof(ITunnelProcessSupervisor))]
public sealed class TunnelProcessSupervisor(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IFrpProviderFactory providerFactory,
    CredentialProtector credentialProtector,
    OperationLock operationLock,
    IOptions<FrpmStorageOptions> storageOptions,
    RunLogBuffer logBuffer,
    ILogger<TunnelProcessSupervisor> logger) : ITunnelProcessSupervisor
{
    private readonly ConcurrentDictionary<Guid, ManagedProcess> _processes = new();
    private readonly ConcurrentDictionary<Guid, TunnelCircuitBreakerState> _circuitBreakers = new();
    private readonly string _dataDirectory = Path.GetFullPath(storageOptions.Value.DataDirectory);

    public Task StartAsync(Guid tunnelId, CancellationToken cancellationToken = default)
    {
        ResetCircuitBreaker(tunnelId);
        return StartCoreAsync(tunnelId, isAutoRetry: false, cancellationToken);
    }

    public TunnelCircuitBreakerState? GetCircuitBreakerStatus(Guid tunnelId) =>
        _circuitBreakers.TryGetValue(tunnelId, out var state) ? state : null;

    public void ResetCircuitBreaker(Guid tunnelId)
    {
        _circuitBreakers.AddOrUpdate(tunnelId,
            _ => new TunnelCircuitBreakerState(),
            (_, state) =>
            {
                state.ConsecutiveFailures = 0;
                state.IsTripped = false;
                state.TripReason = null;
                state.NextRetryTime = null;
                return state;
            });
    }

    public async Task TryAutoRecoverAsync(Guid tunnelId, CancellationToken cancellationToken = default)
    {
        await using var gate = await operationLock.AcquireAsync($"tunnel:{tunnelId}", cancellationToken);
        if (_processes.TryGetValue(tunnelId, out var existing) && !existing.Process.HasExited) return;

        var circuit = _circuitBreakers.GetOrAdd(tunnelId, _ => new TunnelCircuitBreakerState());
        if (circuit.IsTripped)
        {
            logger.LogInformation("隧道 {TunnelId} 熔断保护已开启，跳过自动恢复", tunnelId);
            return;
        }

        if (circuit.NextRetryTime.HasValue && DateTimeOffset.UtcNow < circuit.NextRetryTime.Value)
        {
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var tunnel = await db.Tunnels.Include(x => x.ProviderAccount).SingleOrDefaultAsync(x => x.Id == tunnelId, cancellationToken);
        if (tunnel is null || tunnel.DesiredState != TunnelDesiredState.Running || tunnel.DeletedAt is not null) return;
        if (!tunnel.ProviderAccount.Enabled || tunnel.RemoteState is TunnelRemoteState.RemoteMissing or TunnelRemoteState.Deleted) return;

        var provider = providerFactory.Get(tunnel.ProviderAccount.ProviderType);
        if (circuit.RequiresRemoteKick || provider.Capabilities.SupportsKick)
        {
            try
            {
                logger.LogInformation("隧道 {TunnelId} 恢复前向远端发送强制下线并解除禁用...", tunnelId);
                var credentials = credentialProtector.Unprotect(tunnel.ProviderAccount.ProtectedCredentials) with { AccountId = tunnel.ProviderAccount.Id };
                await provider.KickTunnelAsync(tunnel.ProviderAccount.ApiBaseUrl, credentials, tunnel.RemoteId, cancellationToken);
                await provider.SetTunnelDisabledAsync(tunnel.ProviderAccount.ApiBaseUrl, credentials, tunnel.RemoteId, false, cancellationToken);
                circuit.RequiresRemoteKick = false;
                await Task.Delay(2000, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "隧道 {TunnelId} 恢复前强制下线失败，继续尝试启动", tunnelId);
            }
        }

        try
        {
            await StartCoreInternalAsync(tunnelId, isAutoRetry: true, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "隧道 {TunnelId} 自动恢复启动失败", tunnelId);
        }
    }

    public async Task CheckAndRecoverTunnelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var candidates = await db.Tunnels
                .AsNoTracking()
                .Where(x => x.DesiredState == TunnelDesiredState.Running
                    && x.RuntimeState == TunnelRuntimeState.Failed
                    && x.DeletedAt == null
                    && x.ProviderAccount.Enabled
                    && x.RemoteState != TunnelRemoteState.RemoteMissing
                    && x.RemoteState != TunnelRemoteState.Deleted)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

            foreach (var tunnelId in candidates)
            {
                if (_circuitBreakers.TryGetValue(tunnelId, out var circuit))
                {
                    if (circuit.IsTripped) continue;
                    if (circuit.NextRetryTime.HasValue && DateTimeOffset.UtcNow < circuit.NextRetryTime.Value) continue;
                }

                await TryAutoRecoverAsync(tunnelId, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "巡检自动恢复隧道发生异常");
        }
    }

    private async Task StartCoreAsync(Guid tunnelId, bool isAutoRetry, CancellationToken cancellationToken)
    {
        await using var gate = await operationLock.AcquireAsync($"tunnel:{tunnelId}", cancellationToken);
        await StartCoreInternalAsync(tunnelId, isAutoRetry, cancellationToken);
    }

    private async Task StartCoreInternalAsync(Guid tunnelId, bool isAutoRetry, CancellationToken cancellationToken)
    {
        if (_processes.TryGetValue(tunnelId, out var existing) && !existing.Process.HasExited) return;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var tunnel = await db.Tunnels.Include(x => x.ProviderAccount).SingleOrDefaultAsync(x => x.Id == tunnelId, cancellationToken)
            ?? throw new KeyNotFoundException("隧道不存在。");
        tunnel.DesiredState = TunnelDesiredState.Running;

        if (!tunnel.ProviderAccount.Enabled || tunnel.DeletedAt is not null || tunnel.RemoteState is TunnelRemoteState.RemoteMissing or TunnelRemoteState.Deleted)
        {
            tunnel.RuntimeState = TunnelRuntimeState.Failed;
            tunnel.RuntimeMessage = "供应商账号已禁用或远端隧道不存在。";
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException(tunnel.RuntimeMessage);
        }

        var os = CurrentOs();
        var arch = CurrentArchitecture();
        var package = await db.CliPackages.SingleOrDefaultAsync(x => x.ProviderType == tunnel.ProviderAccount.ProviderType && x.OperatingSystem == os && x.Architecture == arch && x.IsActive && x.DeletedAt == null, cancellationToken);
        if (package is null || !File.Exists(package.ExecutablePath))
        {
            tunnel.RuntimeState = TunnelRuntimeState.Failed;
            tunnel.RuntimeMessage = $"没有适用于 {os}/{arch} 的活动 CLI。";
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException(tunnel.RuntimeMessage);
        }

        tunnel.RuntimeState = TunnelRuntimeState.Starting;
        tunnel.RuntimeMessage = null;
        var run = new TunnelRun
        {
            TunnelId = tunnel.Id,
            CliPackageId = package.Id,
            ExecutablePath = package.ExecutablePath,
            LogPath = string.Empty
        };
        var workingDirectory = Path.Combine(_dataDirectory, "runs", run.Id.ToString("N"));
        var logDirectory = Path.Combine(_dataDirectory, "logs", tunnel.Id.ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        Directory.CreateDirectory(logDirectory);
        run.LogPath = Path.Combine(logDirectory, $"{run.Id:N}.log");

        var provider = providerFactory.Get(tunnel.ProviderAccount.ProviderType);
        var credentials = credentialProtector.Unprotect(tunnel.ProviderAccount.ProtectedCredentials) with { AccountId = tunnel.ProviderAccount.Id };
        var providerTunnel = ToProviderTunnel(tunnel);
        LaunchSpecification specification;
        try
        {
            specification = await provider.PrepareLaunchAsync(tunnel.ProviderAccount.ApiBaseUrl, credentials, providerTunnel, package.ExecutablePath, workingDirectory, cancellationToken);
        }
        catch (Exception ex)
        {
            tunnel.RuntimeState = TunnelRuntimeState.Failed;
            tunnel.RuntimeMessage = ex.Message;
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }

        var startInfo = CreateProcessStartInfo(specification);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("CLI 进程未能启动。");
        }
        catch (Exception ex)
        {
            process.Dispose();
            tunnel.RuntimeState = TunnelRuntimeState.Failed;
            tunnel.RuntimeMessage = ex.Message;
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }

        run.ProcessId = process.Id;
        run.ProcessStartedAt = process.StartTime.ToUniversalTime();
        tunnel.RuntimeState = TunnelRuntimeState.Running;
        tunnel.RuntimeMessage = $"PID {process.Id}";
        db.TunnelRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        var managed = new ManagedProcess(process, run.Id, run.LogPath, specification.SensitiveValues.Where(x => !string.IsNullOrEmpty(x)).ToArray());
        if (!_processes.TryAdd(tunnelId, managed))
        {
            process.Kill(true);
            process.Dispose();
            throw new InvalidOperationException("隧道已由另一个操作启动。");
        }

        managed.Completion = MonitorAsync(tunnelId, managed);
        logger.LogInformation("隧道 {TunnelId} 已启动，PID {ProcessId}{Retry}", tunnelId, process.Id, isAutoRetry ? " (自动重试)" : string.Empty);
    }

    public async Task StopAsync(Guid tunnelId, string reason, CancellationToken cancellationToken = default)
    {
        ResetCircuitBreaker(tunnelId);
        await using var gate = await operationLock.AcquireAsync($"tunnel:{tunnelId}", cancellationToken);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var tunnel = await db.Tunnels.SingleOrDefaultAsync(x => x.Id == tunnelId, cancellationToken)
            ?? throw new KeyNotFoundException("隧道不存在。");
        tunnel.DesiredState = TunnelDesiredState.Stopped;
        tunnel.RuntimeState = TunnelRuntimeState.Stopping;
        tunnel.RuntimeMessage = reason;
        await db.SaveChangesAsync(cancellationToken);

        if (_processes.TryGetValue(tunnelId, out var managed))
        {
            managed.StopRequested = true;
            try
            {
                if (!managed.Process.HasExited) managed.Process.Kill(true);
                await managed.Process.WaitForExitAsync(cancellationToken);
            }
            catch (InvalidOperationException) { }
        }

        await using var finalDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var finalTunnel = await finalDb.Tunnels.SingleAsync(x => x.Id == tunnelId, cancellationToken);
        finalTunnel.RuntimeState = TunnelRuntimeState.Stopped;
        finalTunnel.RuntimeMessage = reason;
        await finalDb.SaveChangesAsync(cancellationToken);
    }

    public async Task StopAllAsync(string reason, bool preserveDesiredState = false, CancellationToken cancellationToken = default)
    {
        foreach (var tunnelId in _processes.Keys.ToArray())
        {
            if (preserveDesiredState)
            {
                await StopForShutdownAsync(tunnelId, reason, cancellationToken);
            }
            else
            {
                await StopAsync(tunnelId, reason, cancellationToken);
            }
        }
    }

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var staleRuns = await db.TunnelRuns.Where(x => x.EndedAt == null).ToListAsync(cancellationToken);
        foreach (var run in staleRuns)
        {
            TryTerminateValidatedStaleProcess(run);
            run.EndedAt = DateTimeOffset.UtcNow;
            run.Summary = "应用启动时清理上一运行实例。";
        }
        await db.SaveChangesAsync(cancellationToken);

        var ids = await db.Tunnels.Where(x => x.DesiredState == TunnelDesiredState.Running && x.DeletedAt == null).Select(x => x.Id).ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            try { await StartAsync(id, cancellationToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "恢复隧道 {TunnelId} 失败", id); }
        }
    }

    private async Task StopForShutdownAsync(Guid tunnelId, string reason, CancellationToken cancellationToken)
    {
        if (!_processes.TryGetValue(tunnelId, out var managed)) return;
        managed.StopRequested = true;
        try
        {
            if (!managed.Process.HasExited) managed.Process.Kill(true);
            await managed.Process.WaitForExitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException) { }

        await using var db = await dbContextFactory.CreateDbContextAsync(CancellationToken.None);
        var tunnel = await db.Tunnels.SingleOrDefaultAsync(x => x.Id == tunnelId, CancellationToken.None);
        if (tunnel is not null)
        {
            tunnel.RuntimeState = TunnelRuntimeState.Stopped;
            tunnel.RuntimeMessage = reason;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task MonitorAsync(Guid tunnelId, ManagedProcess managed)
    {
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        logBuffer.Start(managed.RunId);
        var writerTask = WriteLogAsync(managed, channel.Reader);
        var stdout = PumpAsync(managed, managed.Process.StandardOutput, "OUT", channel.Writer);
        var stderr = PumpAsync(managed, managed.Process.StandardError, "ERR", channel.Writer);

        using var watchdogCts = new CancellationTokenSource();
        var watchdogTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), watchdogCts.Token);
                if (!managed.HasConnected && !managed.StopRequested && managed.LastErrorLine != null && !managed.Process.HasExited)
                {
                    logger.LogWarning("隧道 {TunnelId} 在 30 秒内未成功建立连接且检测到错误，强制终止进程", tunnelId);
                    managed.Process.Kill(true);
                }
            }
            catch (OperationCanceledException) { }
        });

        int? exitCode = null;
        try
        {
            await managed.Process.WaitForExitAsync();
            exitCode = managed.Process.ExitCode;
            watchdogCts.Cancel();
            await Task.WhenAll(stdout, stderr);
        }
        catch (Exception ex) { logger.LogWarning(ex, "监控隧道 {TunnelId} 的进程失败", tunnelId); }
        finally
        {
            watchdogCts.Cancel();
            channel.Writer.TryComplete();
            try
            {
                await writerTask;
            }
            finally
            {
                logBuffer.Complete(managed.RunId);
                _processes.TryRemove(tunnelId, out _);
                managed.Process.Dispose();
            }
        }

        var uptime = DateTimeOffset.UtcNow - managed.StartedAt;
        var circuit = _circuitBreakers.GetOrAdd(tunnelId, _ => new TunnelCircuitBreakerState());

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var run = await db.TunnelRuns.SingleOrDefaultAsync(x => x.Id == managed.RunId);
        var tunnel = await db.Tunnels.SingleOrDefaultAsync(x => x.Id == tunnelId);

        if (managed.StopRequested)
        {
            ResetCircuitBreaker(tunnelId);
            if (run is not null)
            {
                run.EndedAt = DateTimeOffset.UtcNow;
                run.ExitCode = exitCode;
                run.StopRequested = true;
                run.Summary = "已由管理平台停止";
            }
            if (tunnel is not null)
            {
                tunnel.RuntimeState = TunnelRuntimeState.Stopped;
                tunnel.RuntimeMessage = "已由管理平台停止";
            }
        }
        else
        {
            // Process terminated unexpectedly
            if (managed.HasConnected && uptime >= TimeSpan.FromSeconds(60))
            {
                // Previously ran stably for > 60 seconds
                circuit.ConsecutiveFailures = 0;
            }

            circuit.ConsecutiveFailures++;
            circuit.LastFailureTime = DateTimeOffset.UtcNow;
            if (managed.IsOnlineConflict)
            {
                circuit.RequiresRemoteKick = true;
            }

            if (circuit.ConsecutiveFailures >= 5)
            {
                circuit.IsTripped = true;
                circuit.TripReason = $"连续失败 5 次（{managed.LastErrorLine ?? "CLI 异常退出"}），已触发熔断保护暂停自动重试。";
                if (run is not null)
                {
                    run.EndedAt = DateTimeOffset.UtcNow;
                    run.ExitCode = exitCode;
                    run.StopRequested = false;
                    run.Summary = circuit.TripReason;
                }
                if (tunnel is not null)
                {
                    tunnel.RuntimeState = TunnelRuntimeState.Failed;
                    tunnel.RuntimeMessage = circuit.TripReason;
                }
            }
            else
            {
                var backoffSeconds = circuit.ConsecutiveFailures switch
                {
                    1 => 5,
                    2 => 15,
                    3 => 30,
                    4 => 60,
                    _ => 120
                };
                circuit.NextRetryTime = DateTimeOffset.UtcNow.AddSeconds(backoffSeconds);
                var retryMsg = $"运行异常: {managed.LastErrorLine ?? $"CLI 退出码 {exitCode?.ToString() ?? "未知"}"}。将在 {backoffSeconds} 秒后第 {circuit.ConsecutiveFailures} 次重试。";

                if (run is not null)
                {
                    run.EndedAt = DateTimeOffset.UtcNow;
                    run.ExitCode = exitCode;
                    run.StopRequested = false;
                    run.Summary = retryMsg;
                }
                if (tunnel is not null)
                {
                    tunnel.RuntimeState = TunnelRuntimeState.Failed;
                    tunnel.RuntimeMessage = retryMsg;
                }

                _ = ScheduleAutoRecoveryAsync(tunnelId, TimeSpan.FromSeconds(backoffSeconds));
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task ScheduleAutoRecoveryAsync(Guid tunnelId, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay);
            await TryAutoRecoverAsync(tunnelId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "隧道 {TunnelId} 自动恢复触发异常", tunnelId);
        }
    }

    private static async Task PumpAsync(ManagedProcess managed, StreamReader reader, string stream, ChannelWriter<string> writer)
    {
        while (await reader.ReadLineAsync() is { } rawLine)
        {
            var line = rawLine;
            foreach (var secret in managed.SensitiveValues) line = line.Replace(secret, "***", StringComparison.Ordinal);
            line = ProcessLogFormatter.RemoveTerminalSequences(line);

            InspectLine(managed, line);

            await writer.WriteAsync($"{DateTimeOffset.Now:O} [{stream}] {line}");
        }
    }

    private static void InspectLine(ManagedProcess managed, string line)
    {
        if (line.Contains("start proxy success", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("login to server success", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("proxy added", StringComparison.OrdinalIgnoreCase))
        {
            managed.HasConnected = true;
            return;
        }

        if (line.Contains("隧道当前在线", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("API 错误: 隧道当前在线", StringComparison.OrdinalIgnoreCase))
        {
            managed.IsOnlineConflict = true;
            managed.LastErrorLine = "远端隧道当前在线，存在残留会话";
            TriggerProcessKill(managed);
            return;
        }

        if (line.Contains("获取隧道", StringComparison.OrdinalIgnoreCase) && line.Contains("配置失败", StringComparison.OrdinalIgnoreCase))
        {
            managed.LastErrorLine = "获取隧道配置失败";
            TriggerProcessKill(managed);
            return;
        }

        if (line.Contains("请使用“生成启动配置”", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("请使用生成启动配置", StringComparison.OrdinalIgnoreCase))
        {
            managed.LastErrorLine = "提示需在控制台生成启动配置";
            TriggerProcessKill(managed);
            return;
        }

        if (line.Contains("authorization failed", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("token is not valid", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("token invalid", StringComparison.OrdinalIgnoreCase))
        {
            managed.LastErrorLine = "FRP 认证失败，请检查密钥";
            TriggerProcessKill(managed);
            return;
        }

        if (line.Contains("ERR", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            managed.LastErrorLine ??= line;
        }
    }

    private static void TriggerProcessKill(ManagedProcess managed)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500);
                if (!managed.Process.HasExited)
                {
                    managed.Process.Kill(true);
                }
            }
            catch { }
        });
    }

    private async Task WriteLogAsync(ManagedProcess managed, ChannelReader<string> reader)
    {
        await using var stream = new FileStream(managed.LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 16_384, FileOptions.Asynchronous);
        await using var writer = new StreamWriter(stream) { AutoFlush = true };
        await foreach (var line in reader.ReadAllAsync())
        {
            await writer.WriteLineAsync(line);
            logBuffer.Append(managed.RunId, line);
        }
    }

    private static void TryTerminateValidatedStaleProcess(TunnelRun run)
    {
        if (run.ProcessId is not { } pid || run.ProcessStartedAt is not { } startedAt) return;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited) return;

            var targetName = Path.GetFileNameWithoutExtension(run.ExecutablePath);
            bool isMatch = false;

            try
            {
                var actualPath = process.MainModule?.FileName;
                if (actualPath is not null && Path.GetFullPath(actualPath).Equals(Path.GetFullPath(run.ExecutablePath), StringComparison.OrdinalIgnoreCase))
                {
                    isMatch = true;
                }
            }
            catch
            {
                // In Linux / Docker containers, accessing MainModule may throw Win32Exception
            }

            if (!isMatch && string.Equals(process.ProcessName, targetName, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var actualStart = process.StartTime.ToUniversalTime();
                    if (Math.Abs((actualStart - startedAt.UtcDateTime).TotalSeconds) <= 15)
                    {
                        isMatch = true;
                    }
                }
                catch
                {
                    // If process name matches inside a container where start time is inaccessible
                    isMatch = true;
                }
            }

            if (isMatch)
            {
                process.Kill(true);
                process.WaitForExit(3_000);
            }
        }
        catch { }
    }

    private static ProviderTunnel ToProviderTunnel(Tunnel tunnel) => new(
        tunnel.RemoteId, tunnel.Name, tunnel.Remark, tunnel.Type, tunnel.NodeId, tunnel.NodeName,
        tunnel.LocalAddress, tunnel.LocalPort, tunnel.RemoteAddress, tunnel.ProviderStatus, tunnel.ProviderPayloadJson);

    internal static ProcessStartInfo CreateProcessStartInfo(LaunchSpecification specification)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = specification.ExecutablePath,
            WorkingDirectory = specification.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in specification.Arguments) startInfo.ArgumentList.Add(argument);
        foreach (var variable in specification.Environment) startInfo.Environment[variable.Key] = variable.Value;
        return startInfo;
    }

    public static string CurrentOs()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "windows";
        }

        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? "linux"
            : "unsupported";
    }

    public static string CurrentArchitecture() => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant() switch
    {
        "x64" => "amd64",
        "x86" => "386",
        "arm64" => "arm64",
        "arm" => "arm",
        var value => value
    };

    private sealed class ManagedProcess(Process process, Guid runId, string logPath, IReadOnlyList<string> sensitiveValues)
    {
        public Process Process { get; } = process;
        public Guid RunId { get; } = runId;
        public string LogPath { get; } = logPath;
        public IReadOnlyList<string> SensitiveValues { get; } = sensitiveValues;
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
        public bool HasConnected { get; set; }
        public bool IsOnlineConflict { get; set; }
        public string? LastErrorLine { get; set; }
        public bool StopRequested { get; set; }
        public Task Completion { get; set; } = Task.CompletedTask;
    }
}

internal static partial class ProcessLogFormatter
{
    [System.Text.RegularExpressions.GeneratedRegex(@"(?:\x1B\][^\x07]*(?:\x07|\x1B\\))|(?:\x1B\[[0-?]*[ -/]*[@-~])")]
    private static partial System.Text.RegularExpressions.Regex TerminalSequenceRegex();

    internal static string RemoveTerminalSequences(string value) => TerminalSequenceRegex().Replace(value, "");
}
