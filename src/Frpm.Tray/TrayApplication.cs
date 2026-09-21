using System.ComponentModel;
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
    private NativeMenuItem? _startServiceMenuItem;
    private NativeMenuItem? _stopServiceMenuItem;

    public override void OnFrameworkInitializationCompleted()
    {
        _statusMenuItem = new NativeMenuItem("服务状态：检查中") { IsEnabled = false };

        _startServiceMenuItem = new NativeMenuItem("启动 FRPM 服务") { IsEnabled = false };
        _startServiceMenuItem.Click += async (_, _) => await ManageServiceAsync("start", "正在请求启动服务");

        _stopServiceMenuItem = new NativeMenuItem("停止 FRPM 服务") { IsEnabled = false };
        _stopServiceMenuItem.Click += async (_, _) => await ManageServiceAsync("stop", "正在请求停止服务");

        var openMenuItem = new NativeMenuItem("打开管理页面");
        openMenuItem.Click += (_, _) => OpenDashboard();

        var openLogsMenuItem = new NativeMenuItem("打开日志文件夹");
        openLogsMenuItem.Click += (_, _) => OpenLogDirectory();

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
                    _startServiceMenuItem,
                    _stopServiceMenuItem,
                    new NativeMenuItemSeparator(),
                    openMenuItem,
                    openLogsMenuItem,
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
        var isDashboardAvailable = await IsDashboardAvailableAsync();
        var serviceState = await GetServiceStateAsync();
        var status = GetStatusText(isDashboardAvailable, serviceState);

        if (_statusMenuItem is not null)
        {
            _statusMenuItem.Header = status;
        }

        if (_startServiceMenuItem is not null)
        {
            _startServiceMenuItem.IsEnabled = serviceState is WindowsServiceState.Stopped;
        }

        if (_stopServiceMenuItem is not null)
        {
            _stopServiceMenuItem.IsEnabled = serviceState is WindowsServiceState.Running;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.ToolTipText = $"FRPM：{status[5..]}";
        }
    }

    private async Task<bool> IsDashboardAvailableAsync()
    {
        try
        {
            using var response = await _httpClient.GetAsync(new Uri(DashboardUri, "health"));
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    private static async Task<WindowsServiceState> GetServiceStateAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return WindowsServiceState.Unavailable;
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "sc.exe"),
                Arguments = "query FRPM",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await outputTask;

            if (process.ExitCode == 1060)
            {
                return WindowsServiceState.NotInstalled;
            }

            if (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
            {
                return WindowsServiceState.Running;
            }

            if (output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
            {
                return WindowsServiceState.Stopped;
            }
        }
        catch (Win32Exception)
        {
            // The status remains unavailable until the next refresh.
        }

        return WindowsServiceState.Unavailable;
    }

    private static string GetStatusText(bool isDashboardAvailable, WindowsServiceState serviceState) => serviceState switch
    {
        WindowsServiceState.Running when isDashboardAvailable => "服务状态：正在运行",
        WindowsServiceState.Running => "服务状态：正在启动或管理页面不可访问",
        WindowsServiceState.Stopped => "服务状态：已停止",
        WindowsServiceState.NotInstalled when isDashboardAvailable => "服务状态：管理页面可访问，Windows 服务未注册",
        WindowsServiceState.NotInstalled => "服务状态：Windows 服务未注册",
        _ when isDashboardAvailable => "服务状态：管理页面可访问",
        _ => "服务状态：不可访问"
    };

    private async Task ManageServiceAsync(string action, string pendingMessage)
    {
        if (_statusMenuItem is not null)
        {
            _statusMenuItem.Header = $"服务状态：{pendingMessage}";
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "sc.exe"),
                Arguments = $"{action} FRPM",
                Verb = "runas",
                UseShellExecute = true
            });

            if (process is not null)
            {
                await process.WaitForExitAsync();
            }
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            if (_statusMenuItem is not null)
            {
                _statusMenuItem.Header = "服务状态：已取消管理员授权";
            }
        }
        catch (Win32Exception)
        {
            if (_statusMenuItem is not null)
            {
                _statusMenuItem.Header = "服务状态：无法请求服务操作";
            }
        }

        await RefreshStatusAsync();
    }

    private static void OpenDashboard()
    {
        Process.Start(new ProcessStartInfo(DashboardUri.AbsoluteUri) { UseShellExecute = true });
    }

    private static void OpenLogDirectory()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "FRPM",
            "data",
            "logs");
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{logDirectory}\"") { UseShellExecute = true });
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

    private enum WindowsServiceState
    {
        Unavailable,
        NotInstalled,
        Stopped,
        Running
    }
}
