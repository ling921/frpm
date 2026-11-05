using Frpm.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Frpm.Desktop.Tests;

public sealed class LogCleanupTests
{
    [Fact]
    public async Task Export_preserves_more_than_2000_lines_and_rejects_overwriting_managed_data()
    {
        await using var runtime = new TestRuntime();
        await runtime.StartAsync(seed: true);
        var factory = runtime.Host.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var run = await db.TunnelRuns.FirstAsync();
        var content = string.Join('\n', Enumerable.Range(0, 2500).Select(i => $"日志 {i}"));
        await File.WriteAllTextAsync(run.LogPath, content, new System.Text.UTF8Encoding(false));
        await using var destination = new MemoryStream();
        await runtime.Workspace.ExportLogAsync(run.Id, destination);
        Assert.Equal(content, System.Text.Encoding.UTF8.GetString(destination.ToArray()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Workspace.ValidateLogExportDestinationAsync(run.LogPath));
    }

    [Fact]
    public async Task Cleanup_deletes_completed_files_and_records_but_preserves_active_open_and_external_logs()
    {
        await using var runtime = new TestRuntime();
        await runtime.StartAsync(seed: true);
        var factory = runtime.Host.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var seed = await db.TunnelRuns.FirstAsync();
        var logs = Directory.CreateDirectory(Path.Combine(runtime.Directory, "logs")).FullName;
        seed.LogPath = Path.Combine(logs, "completed.log");
        seed.EndedAt = DateTimeOffset.UtcNow;
        var open = new Frpm.Domain.Entities.TunnelRun { TunnelId = seed.TunnelId, CliPackageId = seed.CliPackageId,
            ExecutablePath = seed.ExecutablePath, LogPath = Path.Combine(logs, "open.log"), EndedAt = DateTimeOffset.UtcNow };
        var active = new Frpm.Domain.Entities.TunnelRun { TunnelId = seed.TunnelId, CliPackageId = seed.CliPackageId,
            ExecutablePath = seed.ExecutablePath, LogPath = Path.Combine(logs, "active.log") };
        var external = new Frpm.Domain.Entities.TunnelRun { TunnelId = seed.TunnelId, CliPackageId = seed.CliPackageId,
            ExecutablePath = seed.ExecutablePath, LogPath = Path.Combine(runtime.Directory, "external.log"), EndedAt = DateTimeOffset.UtcNow };
        db.TunnelRuns.AddRange(open, active, external);
        foreach (var path in new[] { seed.LogPath, open.LogPath, active.LogPath, external.LogPath })
            await File.WriteAllTextAsync(path, "preserve or delete by run state");
        await db.SaveChangesAsync();
        var result = await runtime.Workspace.CleanupLogsAsync([open.Id]);
        Assert.Equal(1, result.Deleted);
        // The other seeded run also uses a legacy log outside the logs directory.
        Assert.Equal(2, result.Skipped);
        Assert.False(File.Exists(seed.LogPath));
        Assert.True(File.Exists(open.LogPath));
        Assert.True(File.Exists(active.LogPath));
        Assert.True(File.Exists(external.LogPath));
        Assert.False(await db.TunnelRuns.AsNoTracking().AnyAsync(r => r.Id == seed.Id));
        Assert.True(await db.TunnelRuns.AsNoTracking().AnyAsync(r => r.Id == active.Id));
    }
}
