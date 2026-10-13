using Avalonia.Controls;
using Frpm.Abstractions.Models;

namespace Frpm.Desktop;

internal sealed partial class MainWindow
{
    private async Task RenderRunsAsync()
    {
        var runs = await _workspace.GetRunsAsync(ct: _lifetime.Token);
        BeginPage("运行日志", "最近 200 次本地运行；打开日志窗口可持续跟踪输出");
        PageContent.Children.Add(Actions(ActionButton("打开日志文件夹", () =>
        {
            DesktopShell.OpenDirectory(Path.Combine(_dataDirectory, "logs"));
            return Task.CompletedTask;
        })));
        var filter = Input(watermark: "搜索隧道名称、CLI 版本或运行摘要");
        var list = new StackPanel();
        void UpdateList()
        {
            list.Children.Clear();
            var query = filter.Text?.Trim() ?? string.Empty;
            foreach (var run in runs.Where(item => query.Length == 0 ||
                         $"{item.TunnelName} {item.CliVersion} {item.Summary}".Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                heading.Children.Add(Text(run.TunnelName, size: 17));
                var state = new TextBlock { Text = run.EndedAt is null ? "运行中" : run.ExitCode is null or 0 ? "已结束" : $"异常退出 · {run.ExitCode}" };
                state.Classes.Add("statusText");
                var isRunning = run.EndedAt is null;
                state.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(isRunning ? "#107C10" : run.ExitCode is > 0 ? "#C42B1C" : "#5D5D5D"));
                var pill = new Border { Child = state, Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(isRunning ? "#E8F5E9" : run.ExitCode is > 0 ? "#FDE7E9" : "#F2F2F2")) };
                pill.Classes.Add("statusPill");
                Grid.SetColumn(pill, 1);
                heading.Children.Add(pill);
                var content = Stack(heading,
                    Text($"CLI {run.CliVersion} · PID {run.ProcessId?.ToString() ?? "—"}", hint: true),
                    Text($"开始 {Time(run.StartedAt)} · 结束 {Time(run.EndedAt)}", hint: true));
                if (!string.IsNullOrWhiteSpace(run.Summary)) content.Children.Add(Text(run.Summary, hint: true));
                var actions = new List<Control>();
                if (isRunning)
                    actions.Add(ActionButton("停止隧道", () => _workspace.StopTunnelAsync(run.TunnelId, _lifetime.Token), danger: true));
                actions.Add(ActionButton("查看日志", () => { new LogWindow(_workspace, run).Show(this); return Task.CompletedTask; }));
                content.Children.Add(Actions(actions.ToArray()));
                var row = new Border { Child = content };
                row.Classes.Add("listRow");
                list.Children.Add(row);
            }
            if (list.Children.Count == 0)
                list.Children.Add(Section(Text(runs.Count == 0 ? "尚无运行记录。启动一条隧道后可在这里查看日志。" : "没有匹配的运行记录。")));
        }
        filter.TextChanged += (_, _) => UpdateList();
        PageContent.Children.Add(filter);
        PageContent.Children.Add(list);
        UpdateList();
    }
}
