using Frpm.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Frpm.Data;
using Microsoft.EntityFrameworkCore;

namespace Frpm.Infrastructure.Runtime;

public sealed class StorageCleanupWorker(IOptions<FrpmStorageOptions> options, ILogger<StorageCleanupWorker> logger,
    IDbContextFactory<ApplicationDbContext> dbContextFactory) : BackgroundService
{
    private readonly FrpmStorageOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try { await CleanupAsync(stoppingToken); }
            catch (Exception ex) { logger.LogWarning(ex, "清理过期日志失败"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        var root = Path.Combine(Path.GetFullPath(_options.DataDirectory), "logs");
        if (!Directory.Exists(root)) return;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, _options.LogRetentionDays));
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var completed = (await db.TunnelRuns.Where(r => r.EndedAt != null).Select(r => r.LogPath).ToListAsync(ct))
            .Select(Path.GetFullPath).ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        completed.ExceptWith((await db.TunnelRuns.Where(r => r.EndedAt == null).Select(r => r.LogPath).ToListAsync(ct)).Select(Path.GetFullPath));
        // FileShare.Delete is allowed by the tunnel writer: file deletion alone cannot
        // tell whether a log is active. Only completed runs are eligible for retention.
        var files = new DirectoryInfo(root).EnumerateFiles("*.log", SearchOption.AllDirectories)
            .Where(f => completed.Contains(f.FullName) && (f.Attributes & FileAttributes.ReparsePoint) == 0)
            .OrderBy(x => x.LastWriteTimeUtc).ToList();
        foreach (var file in files.Where(x => x.LastWriteTimeUtc < cutoff.UtcDateTime)) TryDelete(file);
        files = files.Where(x => x.Exists).ToList();
        var total = files.Sum(x => x.Length);
        foreach (var file in files)
        {
            if (total <= _options.MaxLogBytes) break;
            var length = file.Length;
            if (TryDelete(file)) total -= length;
        }
    }

    private static bool TryDelete(FileInfo file)
    {
        try { file.Delete(); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
