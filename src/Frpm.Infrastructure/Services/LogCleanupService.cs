using Frpm.Data;
using Frpm.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Frpm.Infrastructure.Services;

[ScopedService]
public sealed class LogCleanupService(IDbContextFactory<ApplicationDbContext> factory, IOptions<FrpmStorageOptions> options)
{
    public async Task<(int Deleted, int Skipped)> CleanupAsync(IReadOnlyCollection<Guid> excluded, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var runs = await db.TunnelRuns.ToListAsync(ct);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var protectedPaths = runs.Where(r => r.EndedAt is null || excluded.Contains(r.Id))
            .Select(r => Path.GetFullPath(r.LogPath)).ToHashSet(comparer);
        var root = Path.GetFullPath(Path.Combine(options.Value.DataDirectory, "logs"));
        var deleted = 0;
        var skipped = 0;
        foreach (var run in runs.Where(r => r.EndedAt is not null && !excluded.Contains(r.Id)))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var path = Path.GetFullPath(run.LogPath);
                var relative = Path.GetRelativePath(root, path);
                // Never delete imported external files or follow directory/file links.
                if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar)
                    || protectedPaths.Contains(path)) { skipped++; continue; }
                var linked = false;
                for (var current = path; current is not null; current = Path.GetDirectoryName(current))
                {
                    if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) { linked = true; break; }
                    if (comparer.Equals(current, root)) break;
                }
                if (linked) { skipped++; continue; }
                File.Delete(path);
                db.TunnelRuns.Remove(run);
                await db.SaveChangesAsync(ct);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
        }
        return (deleted, skipped);
    }
}
