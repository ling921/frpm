using Frpm.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Frpm.Application;

/// <summary>Imports an offline installation into a fresh desktop data directory.</summary>
public static class FrpmDataImporter
{
    public static async Task ImportAsync(string sourceDirectory, string destinationDirectory, CancellationToken ct = default)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceDirectory));
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationDirectory));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (source.Equals(destination, comparison) || destination.StartsWith(source + Path.DirectorySeparatorChar, comparison)
            || source.StartsWith(destination + Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException("来源与目标数据目录不能相同或互相嵌套。");
        if (!File.Exists(Path.Combine(source, "frpm.db")) || !Directory.Exists(Path.Combine(source, "keys")))
            throw new InvalidOperationException("旧数据目录必须同时包含 frpm.db 和 keys 目录。请先停止旧版 FRPM。");
        Directory.CreateDirectory(destination);
        if (Directory.EnumerateFileSystemEntries(destination).Any(path => Path.GetFileName(path) is not ("desktop.lock" or "startup-error.log")))
            throw new InvalidOperationException("目标数据目录已有数据。请使用 --data-dir 指定一个新的空目录，避免覆盖现有数据。");

        var staging = Path.GetFullPath(Path.Combine(destination, $"import-staging-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false
            }))
            {
                ct.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(source, file);
                if (relative is "frpm.db" or "frpm.db-wal" or "frpm.db-shm" or "desktop.lock" or "startup-error.log") continue;
                var target = Path.Combine(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            // Back up SQLite through its API so the database and WAL form one consistent snapshot.
            var stagingDb = Path.Combine(staging, "frpm.db");
            await using (var from = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(source, "frpm.db"), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            await using (var to = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = stagingDb, Pooling = false }.ToString()))
            {
                await from.OpenAsync(ct);
                await to.OpenAsync(ct);
                from.BackupDatabase(to);
            }
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder { DataSource = stagingDb, Pooling = false }.ToString()).Options;
            await using (var db = new ApplicationDbContext(options))
            {
                string Rebase(string path) => path.Equals(source, comparison) ? destination
                    : path.StartsWith(source + Path.DirectorySeparatorChar, comparison)
                        ? Path.Combine(destination, Path.GetRelativePath(source, path)) : path;
                foreach (var package in await db.CliPackages.ToListAsync(ct))
                {
                    package.InstallDirectory = Rebase(package.InstallDirectory);
                    package.ExecutablePath = Rebase(package.ExecutablePath);
                }
                foreach (var run in await db.TunnelRuns.ToListAsync(ct))
                {
                    run.ExecutablePath = Rebase(run.ExecutablePath);
                    run.LogPath = Rebase(run.LogPath);
                }
                await db.SaveChangesAsync(ct);
            }
            foreach (var entry in Directory.EnumerateFileSystemEntries(staging))
            {
                var target = Path.Combine(destination, Path.GetFileName(entry));
                if (Directory.Exists(entry)) Directory.Move(entry, target);
                else File.Move(entry, target);
            }
        }
        finally
        {
            // Only the newly created, direct staging child may be removed recursively.
            if (string.Equals(Path.GetDirectoryName(staging), destination, comparison) && Directory.Exists(staging))
                Directory.Delete(staging, true);
        }
    }
}
