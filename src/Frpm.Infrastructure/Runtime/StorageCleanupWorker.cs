using Frpm.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Frpm.Infrastructure.Runtime;

public sealed class StorageCleanupWorker(IOptions<FrpmStorageOptions> options, ILogger<StorageCleanupWorker> logger) : BackgroundService
{
    private readonly FrpmStorageOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try { Cleanup(); }
            catch (Exception ex) { logger.LogWarning(ex, "清理过期日志失败"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private void Cleanup()
    {
        var root = Path.Combine(Path.GetFullPath(_options.DataDirectory), "logs");
        if (!Directory.Exists(root)) return;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, _options.LogRetentionDays));
        var files = new DirectoryInfo(root).EnumerateFiles("*.log", SearchOption.AllDirectories).OrderBy(x => x.LastWriteTimeUtc).ToList();
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
