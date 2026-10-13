using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Frpm.Abstractions.Models;
using Frpm.Application;
using FluentAvalonia.UI.Controls;

namespace Frpm.Desktop;

internal sealed partial class MainWindow : Window
{
    private readonly FrpmWorkspace _workspace;
    private readonly DesktopOAuthFlow _oauth;
    private readonly Func<Task> _exit;
    private readonly string _dataDirectory;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _ready;
    private bool _busy;
    private string _page = "dashboard";
    private string? _backTarget;
    private readonly Stack<string> _navigationHistory = new();
    private string _tunnelFilter = string.Empty;
    public bool TrayAvailable { get; set; }
    public bool AllowClose { get; set; }

    public MainWindow(FrpmWorkspace workspace, DesktopOAuthFlow oauth, string dataDirectory, Func<Task> exit)
    {
        _workspace = workspace;
        _oauth = oauth;
        _exit = exit;
        _dataDirectory = dataDirectory;
        InitializeComponent();
        Icon = DesktopShell.Icon();
        BackButton.Click += (_, _) => NavigateBack();
        PaneToggleButton.Click += (_, _) => Navigation.IsPaneOpen = !Navigation.IsPaneOpen;
        Navigation.PropertyChanged += (_, args) =>
        {
            if (args.Property.Name == nameof(Navigation.IsPaneOpen)) UpdateTrayInfoVisibility();
        };
        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximizeButton.Click += (_, _) => ToggleMaximized();
        CloseButton.Click += (_, _) => Close();
        TitleDragRegion.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(args);
        };
        PropertyChanged += (_, args) =>
        {
            if (args.Property == WindowStateProperty) UpdateCaptionGlyph();
        };
        AttachResizeGrip(ResizeNorthWest, WindowEdge.NorthWest, StandardCursorType.TopLeftCorner);
        AttachResizeGrip(ResizeNorth, WindowEdge.North, StandardCursorType.TopSide);
        AttachResizeGrip(ResizeNorthEast, WindowEdge.NorthEast, StandardCursorType.TopRightCorner);
        AttachResizeGrip(ResizeWest, WindowEdge.West, StandardCursorType.LeftSide);
        AttachResizeGrip(ResizeEast, WindowEdge.East, StandardCursorType.RightSide);
        AttachResizeGrip(ResizeSouthWest, WindowEdge.SouthWest, StandardCursorType.BottomLeftCorner);
        AttachResizeGrip(ResizeSouth, WindowEdge.South, StandardCursorType.BottomSide);
        AttachResizeGrip(ResizeSouthEast, WindowEdge.SouthEast, StandardCursorType.BottomRightCorner);
        Closing += async (_, args) =>
        {
            if (AllowClose) return;
            args.Cancel = true;
            if (TrayAvailable) Hide();
            else if (await ConfirmAsync("退出 FRPM", "当前桌面未提供系统托盘。退出会停止本地隧道，是否退出？")) await _exit();
        };
        Navigation.SelectionChanged += async (_, args) =>
        {
            if (!_ready || _busy) return;
            var nextPage = (args.SelectedItem as NavigationViewItem)?.Tag?.ToString() ?? "dashboard";
            if (nextPage != _page)
            {
                if (_backTarget == nextPage) _backTarget = null;
                else _navigationHistory.Push(_page);
                _page = nextPage;
            }
            UpdateBackButton();
            await RunActionAsync(() => RenderPageAsync(), "");
        };
        Navigation.BackRequested += (_, _) => NavigateBack();
        RefreshButton.Click += async (_, _) => await RunActionAsync(() => RenderPageAsync(), "已刷新");
        _refreshTimer.Tick += async (_, _) =>
        {
            if (_busy || !_ready || _page is not ("dashboard" or "tunnels" or "runs") || PageContent.IsKeyboardFocusWithin) return;
            await RunActionAsync(() => RenderPageAsync(), "");
        };
    }

    public async Task InitializeAsync()
    {
        _ready = true;
        TrayInfo.Text = TrayAvailable ? "关闭窗口后继续运行" : "当前桌面未提供托盘";
        UpdateTrayInfoVisibility();
        try { ApplyTheme(await _workspace.GetPreferencesAsync(_lifetime.Token)); }
        catch (Exception ex) { ShowStatus($"读取主题失败：{ex.Message}", InfoBarSeverity.Warning); }
        await RunActionAsync(() => RenderPageAsync(), TrayAvailable
            ? "已就绪 · 关闭窗口会保留托盘和隧道运行" : "已就绪 · 当前桌面未提供托盘，关闭窗口时会询问是否退出");
        _refreshTimer.Start();
    }

    public void StopRefreshing()
    {
        _ready = false;
        _refreshTimer.Stop();
        _lifetime.Cancel();
    }

    public void ReportError(Exception exception)
    {
        ShowStatus($"操作失败：{exception.GetBaseException().Message}", InfoBarSeverity.Error);
        if (!IsVisible) { Show(); Activate(); }
    }

    public async Task RunActionAsync(Func<Task> action, string successMessage = "操作已完成")
    {
        if (_busy || _lifetime.IsCancellationRequested) return;
        _busy = true;
        BusyBar.IsVisible = true;
        Navigation.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        try
        {
            await action();
            if (!string.IsNullOrWhiteSpace(successMessage)) ShowStatus(successMessage, InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            if (!_lifetime.IsCancellationRequested) ShowStatus("操作已取消或等待超时。", InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
        finally
        {
            _busy = false;
            BusyBar.IsVisible = false;
            Navigation.IsEnabled = true;
            RefreshButton.IsEnabled = true;
        }
    }

    private Task RenderPageAsync() => _page switch
    {
        "providers" => RenderProvidersAsync(),
        "tunnels" => RenderTunnelsAsync(),
        "packages" => RenderPackagesAsync(),
        "runs" => RenderRunsAsync(),
        "settings" => RenderSettingsAsync(),
        _ => RenderDashboardAsync()
    };

    private void BeginPage(string title, string subtitle)
    {
        PageTitle.Text = title;
        PageSubtitle.Text = subtitle;
        PageContent.Children.Clear();
    }

    private Button ActionButton(string title, Func<Task> action, bool primary = false, bool danger = false)
    {
        var button = new Button { Content = title, Margin = new Thickness(0, 0, 8, 4) };
        if (primary) button.Classes.Add("primary");
        if (danger) button.Classes.Add("danger");
        button.Click += async (_, _) => await RunActionAsync(async () =>
        {
            await action();
            await RenderPageAsync();
        });
        return button;
    }

    private static StackPanel Stack(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    private static WrapPanel Actions(params Control[] children)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    private static TextBlock Text(string text, bool hint = false, double size = 14)
    {
        var control = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = size };
        if (hint) control.Classes.Add("hint");
        return control;
    }

    private static Border Section(Control content)
    {
        var border = new Border { Child = content };
        border.Classes.Add("section");
        return border;
    }

    private static string Time(DateTimeOffset? value) => value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "尚未执行";

    private Button NavigationButton(string title, string page, bool primary = false)
    {
        var button = new Button { Content = title, Margin = new Thickness(0, 0, 8, 4) };
        if (primary) button.Classes.Add("primary");
        button.Click += (_, _) => Navigation.SelectedItem = NavigationItem(page);
        return button;
    }

    public async Task<bool> ConfirmAsync(string title, string message, Window? owner = null)
    {
        var dialog = new FormDialog(title, Stack(Text(message)), () => Task.CompletedTask, "确定") { Height = 280 };
        return await dialog.ShowDialog<bool>(owner ?? this);
    }

    private void ShowStatus(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        StatusMessage.Text = message;
        StatusMessage.Foreground = new SolidColorBrush(Color.Parse(severity switch
        {
            InfoBarSeverity.Error => "#C42B1C",
            InfoBarSeverity.Warning => "#8A5A00",
            InfoBarSeverity.Success => "#107C10",
            _ => "#5D5D5D"
        }));
        StatusMessage.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    private void NavigateBack()
    {
        if (!_navigationHistory.TryPop(out var previous)) return;
        _backTarget = previous;
        Navigation.SelectedItem = NavigationItem(previous);
        UpdateBackButton();
    }

    private void UpdateBackButton()
    {
        BackButton.IsEnabled = _navigationHistory.Count > 0;
        Navigation.IsBackEnabled = _navigationHistory.Count > 0;
    }

    private void ToggleMaximized() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void UpdateTrayInfoVisibility() => TrayInfo.IsVisible = Navigation.IsPaneOpen;

    private void UpdateCaptionGlyph()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeGlyph.IsVisible = !maximized;
        RestoreGlyph.IsVisible = maximized;
        var allowResize = !maximized;
        ResizeNorthWest.IsVisible = allowResize;
        ResizeNorth.IsVisible = allowResize;
        ResizeNorthEast.IsVisible = allowResize;
        ResizeWest.IsVisible = allowResize;
        ResizeEast.IsVisible = allowResize;
        ResizeSouthWest.IsVisible = allowResize;
        ResizeSouth.IsVisible = allowResize;
        ResizeSouthEast.IsVisible = allowResize;
        ToolTip.SetTip(MaximizeButton, maximized ? "还原" : "最大化");
    }

    private void AttachResizeGrip(Control grip, WindowEdge edge, StandardCursorType cursor)
    {
        grip.Cursor = new Cursor(cursor);
        grip.PointerPressed += (_, args) =>
        {
            if (WindowState == WindowState.Normal && args.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginResizeDrag(edge, args);
        };
    }

    private Control NavigationItem(string page) => page switch
    {
        "providers" => ProvidersNavigationItem,
        "tunnels" => TunnelsNavigationItem,
        "packages" => PackagesNavigationItem,
        "runs" => RunsNavigationItem,
        "settings" => SettingsNavigationItem,
        _ => DashboardNavigationItem
    };

    private async Task RenderDashboardAsync()
    {
        var stats = await _workspace.GetDashboardAsync(_lifetime.Token);
        var packages = await _workspace.GetPackagesAsync(_lifetime.Token);
        var tunnelStates = await _workspace.GetTunnelsAsync(_lifetime.Token);
        var accounts = await _workspace.GetAccountsAsync(_lifetime.Token);
        BeginPage("概览", "所有供应商和本地隧道的运行状态");
        var tiles = new UniformGrid { Columns = 5, Margin = new Thickness(0, 4, 0, 12) };
        var metrics = new[] { ("供应商账号", stats.AccountCount), ("隧道", stats.TunnelCount),
            ("运行中", stats.RunningCount), ("启动失败", stats.FailedCount), ("远端缺失", stats.RemoteMissingCount) };
        foreach (var (item, index) in metrics.Select((value, index) => (value, index)))
        {
            var value = new TextBlock { Text = item.Item2.ToString() };
            value.Classes.Add("metricValue");
            if (item.Item2 > 0)
                value.Classes.Add(index switch { 2 => "metricGood", 3 => "metricBad", 4 => "metricWarning", _ => "metricNeutral" });
            var metric = new Border { Child = Stack(Text(item.Item1, hint: true), value) };
            metric.Classes.Add("metric");
            tiles.Children.Add(metric);
        }
        PageContent.Children.Add(tiles);
        var nextStep = stats.AccountCount == 0
            ? "先添加供应商账号，之后即可同步隧道配置。"
            : !accounts.Any(account => account.Enabled)
                ? "请先启用一个供应商账号，然后再同步隧道配置。"
            : packages.Count == 0
                ? "供应商账号已就绪。安装适用于当前电脑的 frpc 后即可运行隧道。"
                : stats.TunnelCount == 0
                    ? "CLI 已就绪。同步供应商账号以导入隧道，或手动创建隧道。"
                    : "从隧道列表查看运行状态、调整映射并打开实时日志。";
        var actions = new List<Control>();
        if (stats.AccountCount == 0)
            actions.Add(NavigationButton("添加供应商账号", "providers", primary: true));
        else if (!accounts.Any(account => account.Enabled))
            actions.Add(NavigationButton("启用供应商账号", "providers", primary: true));
        else if (packages.Count == 0)
            actions.Add(NavigationButton("安装 CLI 版本", "packages", primary: true));
        else if (stats.TunnelCount == 0)
        {
            actions.Add(ActionButton("同步供应商账号", () => _workspace.SyncAllAsync(_lifetime.Token), primary: true));
            actions.Add(NavigationButton("创建隧道", "tunnels"));
        }
        else
            actions.Add(NavigationButton("查看隧道", "tunnels", primary: true));

        if (accounts.Any(account => account.Enabled))
            actions.Add(ActionButton("同步全部账号", () => _workspace.SyncAllAsync(_lifetime.Token)));
        if (tunnelStates.Any(tunnel => tunnel.RuntimeState is not (Frpm.Domain.Enums.TunnelRuntimeState.Running or Frpm.Domain.Enums.TunnelRuntimeState.Starting)))
            actions.Add(ActionButton("启动未运行隧道", () => _workspace.SetAllTunnelsRunningAsync(true, _lifetime.Token), primary: stats.RunningCount == 0));
        if (tunnelStates.Any(tunnel => tunnel.RuntimeState is not (Frpm.Domain.Enums.TunnelRuntimeState.Stopped or Frpm.Domain.Enums.TunnelRuntimeState.Stopping)))
            actions.Add(ActionButton("停止全部隧道", async () =>
            {
                if (await ConfirmAsync("停止全部隧道", "将中断所有正在运行的本地隧道，是否继续？"))
                    await _workspace.SetAllTunnelsRunningAsync(false, _lifetime.Token);
            }, danger: true));
        PageContent.Children.Add(Section(Stack(Text("下一步", size: 19), Text(nextStep), Actions(actions.ToArray()),
            Text($"本机数据目录：{_dataDirectory}", hint: true))));
    }

    private static void ApplyTheme(UserPreferencesModel preferences)
    {
        if (Avalonia.Application.Current is not { } app) return;
        app.RequestedThemeVariant = preferences.ThemeMode switch
        {
            "dark" => ThemeVariant.Dark, "light" => ThemeVariant.Light, _ => ThemeVariant.Default
        };
        DesktopApplication.SetAccent(preferences.ThemeMode == "dark"
            ? preferences.ThemeDarkPrimaryColor : preferences.ThemeLightPrimaryColor);
        DesktopApplication.SetPalette(ThemeVariant.Light, preferences.ThemeLightPrimaryColor, "#F3F3F3");
        DesktopApplication.SetPalette(ThemeVariant.Dark, preferences.ThemeDarkPrimaryColor, "#202020");
    }
}
