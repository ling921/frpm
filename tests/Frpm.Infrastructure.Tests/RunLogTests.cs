using Frpm.Infrastructure.Providers;
using Frpm.Infrastructure.Runtime;
using Frpm.Infrastructure.Services;
using System.Text;

namespace Frpm.Infrastructure.Tests;

public sealed class RunLogTests
{
    [Theory]
    [InlineData("\u001b[32mINFO\u001b[0m 隧道启动成功", "INFO 隧道启动成功")]
    [InlineData("\u001b]0;LoliaFrp\u0007日志", "日志")]
    [InlineData("普通日志", "普通日志")]
    public void Process_log_formatter_removes_terminal_sequences(string value, string expected)
    {
        Assert.Equal(expected, ProcessLogFormatter.RemoveTerminalSequences(value));
    }

    [Theory]
    [InlineData(
        "2026-08-25T13:28:49.7394603+08:00 [OUT] 2026/08/25 13:28:49 [I] 隧道启动成功",
        "2026/08/25 13:28:49 [I] 隧道启动成功")]
    [InlineData(
        "2026-08-25T13:28:49.7394603+08:00 [ERR] connection failed",
        "connection failed")]
    [InlineData("CLI 原始输出", "CLI 原始输出")]
    public void Page_log_removes_only_frpm_persistence_prefix(string persisted, string expected)
    {
        Assert.Equal(expected, LogDisplayFormatter.Format(persisted));
    }

    [Fact]
    public void Redirected_cli_output_is_explicitly_decoded_as_utf8()
    {
        var specification = new LaunchSpecification(
            "frpc",
            ".",
            ["-c", "frpc.toml"],
            new Dictionary<string, string?>(),
            []);

        var startInfo = TunnelProcessSupervisor.CreateProcessStartInfo(specification);

        Assert.Same(Encoding.UTF8, startInfo.StandardOutputEncoding);
        Assert.Same(Encoding.UTF8, startInfo.StandardErrorEncoding);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
    }

    [Fact]
    public void Active_run_reads_new_lines_from_bounded_memory_buffer()
    {
        var runId = Guid.NewGuid();
        var buffer = new RunLogBuffer();
        buffer.Start(runId);
        buffer.Append(runId, "line-0");
        buffer.Append(runId, "line-1");
        buffer.Append(runId, "line-2");

        Assert.True(buffer.TryRead(runId, 1, 10, out var tail));
        Assert.Equal(3, tail.NextCursor);
        Assert.Equal(["line-1", "line-2"], tail.Lines);

        buffer.Complete(runId);
        Assert.False(buffer.TryRead(runId, 3, 10, out _));
    }

    [Fact]
    public void Cursor_older_than_memory_window_falls_back_to_file()
    {
        var runId = Guid.NewGuid();
        var buffer = new RunLogBuffer();
        buffer.Start(runId);
        for (var index = 0; index < 2_005; index++)
        {
            buffer.Append(runId, $"line-{index}");
        }

        Assert.False(buffer.TryRead(runId, 0, 10, out _));
        Assert.True(buffer.TryRead(runId, 5, 2, out var tail));
        Assert.Equal(["line-5", "line-6"], tail.Lines);
    }

    [Fact]
    public async Task File_tail_can_read_while_writer_keeps_log_open()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"frpm-log-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "active.log");
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete,
                4_096,
                FileOptions.Asynchronous);
            await using var writer = new StreamWriter(stream) { AutoFlush = true };
            await writer.WriteLineAsync("first");
            await writer.WriteLineAsync("second");

            var tail = await LogFileTailReader.ReadAsync(path, 0, 10, CancellationToken.None);

            Assert.Equal(2, tail.NextCursor);
            Assert.Equal(["first", "second"], tail.Lines);
            Assert.True(tail.EndOfFile);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
