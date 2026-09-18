using Frpm.Abstractions.Models;
using Frpm.Abstractions.Services;
using Frpm.Data;
using Frpm.Infrastructure.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Frpm.Infrastructure.Services;

[ScopedService(typeof(ILogService))]
public sealed class LogService(IDbContextFactory<ApplicationDbContext> dbContextFactory, RunLogBuffer logBuffer) : ILogService
{
    public async Task<LogTailModel> TailAsync(Guid runId, long cursor = 0, int maxLines = 300, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var path = await db.TunnelRuns.Where(x => x.Id == runId).Select(x => x.LogPath).SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("运行记录不存在。");
        maxLines = Math.Clamp(maxLines, 1, 1000);
        if (logBuffer.TryRead(runId, cursor, maxLines, out var buffered))
        {
            return new(runId, buffered.NextCursor, buffered.Lines.Select(LogDisplayFormatter.Format).ToList(), false);
        }

        if (!File.Exists(path)) return new(runId, cursor, [], true);
        var fileTail = await LogFileTailReader.ReadAsync(path, cursor, maxLines, cancellationToken);
        return new(runId, fileTail.NextCursor, fileTail.Lines.Select(LogDisplayFormatter.Format).ToList(), fileTail.EndOfFile);
    }
}

internal static class LogDisplayFormatter
{
    private static readonly string[] StreamPrefixes = ["[OUT] ", "[ERR] "];

    public static string Format(string persistedLine)
    {
        var separator = persistedLine.IndexOf(' ');
        if (separator <= 0
            || !DateTimeOffset.TryParseExact(
                persistedLine.AsSpan(0, separator),
                "O",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out _))
        {
            return persistedLine;
        }

        var remainder = persistedLine[(separator + 1)..];
        foreach (var prefix in StreamPrefixes)
        {
            if (remainder.StartsWith(prefix, StringComparison.Ordinal))
            {
                return remainder[prefix.Length..];
            }
        }

        return persistedLine;
    }
}

internal static class LogFileTailReader
{
    public static async Task<LogFileTail> ReadAsync(string path, long cursor, int maxLines, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            16_384,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream);
        var lines = new List<string>(maxLines);
        long index = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (index++ < cursor) continue;
            lines.Add(line);
            if (lines.Count >= maxLines) break;
        }

        return new(cursor + lines.Count, lines, lines.Count < maxLines);
    }
}

internal readonly record struct LogFileTail(long NextCursor, IReadOnlyList<string> Lines, bool EndOfFile);
