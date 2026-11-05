using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Frpm.Abstractions.Models;
using Frpm.Application;

namespace Frpm.Desktop;

internal sealed class LogWindow : Window
{
    internal Guid RunId => _run.Id;
    private readonly CancellationTokenSource _stop = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly TextBox _output = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
        FontFamily = new FontFamily("Consolas,DejaVu Sans Mono,monospace"), FontSize = 13 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly List<string> _lines = [];
    private readonly FrpmWorkspace _workspace;
    private readonly TunnelRunModel _run;
    private long _cursor;
    private bool _loading;

    public LogWindow(FrpmWorkspace workspace, TunnelRunModel run)
    {
        _workspace = workspace;
        _run = run;
        Title = $"{run.TunnelName} · FRPM 运行日志";
        Width = 940;
        Height = 620;
        MinWidth = 600;
        MinHeight = 400;
        Icon = DesktopShell.Icon();
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var copy = new Button { Content = "复制日志", Cursor = new Cursor(StandardCursorType.Hand) };
        copy.Click += async (_, _) => await ExportAsync(async () => { if (Clipboard is not null) await Clipboard.SetTextAsync(_output.Text ?? ""); });
        var save = new Button { Content = "导出完整日志", Margin = new Thickness(8, 0, 0, 0), Cursor = new Cursor(StandardCursorType.Hand) };
        save.Click += async (_, _) => await ExportAsync(async () =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出完整日志", SuggestedFileName = $"frpm-{run.Id:N}.log", DefaultExtension = "log"
            });
            if (file is null) return;
            await _workspace.ValidateLogExportDestinationAsync(file.TryGetLocalPath());
            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek) { stream.SetLength(0); stream.Position = 0; }
            await _workspace.ExportLogAsync(run.Id, stream, _stop.Token);
            _status.Text = "完整日志已导出。";
        });
        var follow = new CheckBox { Content = "跟随日志", IsChecked = true, Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Children = { copy, save, follow } };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(18) };
        grid.Children.Add(new TextBlock { Text = $"{run.TunnelName} · CLI {run.CliVersion} · {run.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}", FontSize = 17, Margin = new Thickness(0, 0, 0, 12) });
        Grid.SetRow(actions, 1);
        Grid.SetRow(_output, 2);
        Grid.SetRow(_status, 3);
        _output.Margin = new Thickness(0, 12);
        grid.Children.Add(actions);
        grid.Children.Add(_output);
        grid.Children.Add(_status);
        Content = grid;
        Opened += async (_, _) => { await RefreshAsync(follow.IsChecked == true); _timer.Start(); };
        _timer.Tick += async (_, _) => await RefreshAsync(follow.IsChecked == true);
        Closed += (_, _) => { _stop.Cancel(); _timer.Stop(); };
    }

    private async Task ExportAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { _status.Text = $"无法导出日志：{ex.Message}"; }
    }

    private async Task RefreshAsync(bool follow)
    {
        if (_loading || _stop.IsCancellationRequested) return;
        _loading = true;
        try
        {
            var tail = await _workspace.TailLogAsync(_run.Id, _cursor, _stop.Token);
            _cursor = tail.NextCursor;
            _lines.AddRange(tail.Lines);
            if (_lines.Count > 2000) _lines.RemoveRange(0, _lines.Count - 2000);
            if (tail.Lines.Count > 0)
            {
                _output.Text = string.Join(Environment.NewLine, _lines);
                if (follow) _output.CaretIndex = _output.Text.Length;
            }
            _status.Text = $"显示最近 {_lines.Count} 行；复制包含当前显示内容，导出包含完整日志文件。";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _status.Text = $"读取日志失败：{ex.Message}"; }
        finally { _loading = false; }
    }
}
