using Frpm.Data;
using Microsoft.EntityFrameworkCore;
using Frpm.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Frpm.Infrastructure.Services;

[ScopedService]
public sealed class LogFileExportService(IDbContextFactory<ApplicationDbContext> factory, IOptions<FrpmStorageOptions> options)
{
    public void ValidateDestination(string? localPath)
    {
        if (localPath is null) return;
        var relative = Path.GetRelativePath(Path.GetFullPath(options.Value.DataDirectory), Path.GetFullPath(localPath));
        if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar))
            throw new InvalidOperationException("请选择 FRPM 数据目录之外的导出位置，避免覆盖正在使用的日志或应用数据。");
    }

    public async Task CopyAsync(Guid runId, Stream destination, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var path = await db.TunnelRuns.Where(r => r.Id == runId).Select(r => r.LogPath).SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("运行记录不存在，可能已被清理。");
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        // Snapshot the length: a continuously running tunnel must not keep export open.
        var remaining = source.Length;
        var buffer = new byte[65536];
        while (remaining > 0)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);
            if (read == 0) break;
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            remaining -= read;
        }
    }
}
