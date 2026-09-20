using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Frpm.Tray;

internal sealed class TrayApplication : Application
{
    private static readonly Uri DashboardUri = new("http://127.0.0.1:8080/");
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(3) };
    private DispatcherTimer? _statusTimer;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _statusMenuItem;

    public override void OnFrameworkInitializationCompleted()
    {
        _statusMenuItem = new NativeMenuItem("服务状态：检查中") { IsEnabled = false };

        var openMenuItem = new NativeMenuItem("打开管理页面");
        openMenuItem.Click += (_, _) => OpenDashboard();

        var exitMenuItem = new NativeMenuItem("退出托盘");
        exitMenuItem.Click += (_, _) => ExitTray();

        _trayIcon = new TrayIcon
        {
            Icon = TrayIconImage.Create(),
            ToolTipText = "FRPM：正在检查服务状态",
            Menu = new NativeMenu
            {
                Items =
                {
                    _statusMenuItem,
                    new NativeMenuItemSeparator(),
                    openMenuItem,
                    new NativeMenuItemSeparator(),
                    exitMenuItem
                }
            }
        };
        _trayIcon.Clicked += (_, _) => OpenDashboard();
        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync();
        _statusTimer.Start();
        _ = RefreshStatusAsync();

        base.OnFrameworkInitializationCompleted();
    }

    private async Task RefreshStatusAsync()
    {
        var isRunning = false;
        try
        {
            using var response = await _httpClient.GetAsync(new Uri(DashboardUri, "health"));
            isRunning = response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            // The service may be starting, stopped, or unavailable.
        }
        catch (TaskCanceledException)
        {
            // A slow response is shown as unavailable until the next refresh.
        }

        var status = isRunning ? "服务状态：正在运行" : "服务状态：未运行或不可访问";
        if (_statusMenuItem is not null)
        {
            _statusMenuItem.Header = status;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.ToolTipText = $"FRPM：{(isRunning ? "服务正在运行" : "服务不可访问")}";
        }
    }

    private static void OpenDashboard()
    {
        Process.Start(new ProcessStartInfo(DashboardUri.AbsoluteUri) { UseShellExecute = true });
    }

    private void ExitTray()
    {
        _statusTimer?.Stop();
        _trayIcon?.Dispose();
        _httpClient.Dispose();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
