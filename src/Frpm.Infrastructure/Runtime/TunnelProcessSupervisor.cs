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
    private readonly string _dataDirectory = Path.GetFullPath(storageOptions.Value.DataDirectory);

    public async Task StartAsync(Guid tunnelId, CancellationToken cancellationToken = default)
    {
        await using var gate = await operationLock.AcquireAsync($"tunnel:{tunnelId}", cancellationToken);
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
        logger.LogInformation("隧道 {TunnelId} 已启动，PID {ProcessId}", tunnelId, process.Id);
    }

    public async Task StopAsync(Guid tunnelId, string reason, CancellationToken cancellationToken = default)
    {
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
        var stdout = PumpAsync(managed.Process.StandardOutput, "OUT", channel.Writer, managed.SensitiveValues);
        var stderr = PumpAsync(managed.Process.StandardError, "ERR", channel.Writer, managed.SensitiveValues);
        int? exitCode = null;
        try
        {
            await managed.Process.WaitForExitAsync();
            exitCode = managed.Process.ExitCode;
            await Task.WhenAll(stdout, stderr);
        }
        catch (Exception ex) { logger.LogWarning(ex, "监控隧道 {TunnelId} 的进程失败", tunnelId); }
        finally
        {
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

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var run = await db.TunnelRuns.SingleOrDefaultAsync(x => x.Id == managed.RunId);
        var tunnel = await db.Tunnels.SingleOrDefaultAsync(x => x.Id == tunnelId);
        if (run is not null)
        {
            run.EndedAt = DateTimeOffset.UtcNow;
            run.ExitCode = exitCode;
            run.StopRequested = managed.StopRequested;
            run.Summary = managed.StopRequested ? "已由管理平台停止" : $"CLI 意外退出，退出码 {exitCode?.ToString() ?? "未知"}";
        }
        if (tunnel is not null)
        {
            tunnel.RuntimeState = managed.StopRequested ? TunnelRuntimeState.Stopped : TunnelRuntimeState.Failed;
            tunnel.RuntimeMessage = run?.Summary;
        }
        await db.SaveChangesAsync();
    }

    private static async Task PumpAsync(StreamReader reader, string stream, ChannelWriter<string> writer, IReadOnlyList<string> sensitiveValues)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            foreach (var secret in sensitiveValues) line = line.Replace(secret, "***", StringComparison.Ordinal);
            line = ProcessLogFormatter.RemoveTerminalSequences(line);
            await writer.WriteAsync($"{DateTimeOffset.Now:O} [{stream}] {line}");
        }
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
            var actualStart = process.StartTime.ToUniversalTime();
            var actualPath = process.MainModule?.FileName;
            if (Math.Abs((actualStart - startedAt.UtcDateTime).TotalSeconds) <= 2 &&
                actualPath is not null && Path.GetFullPath(actualPath).Equals(Path.GetFullPath(run.ExecutablePath), StringComparison.OrdinalIgnoreCase))
            {
                process.Kill(true);
                process.WaitForExit(5_000);
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
