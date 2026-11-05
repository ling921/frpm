using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.Styling;
using Frpm.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Frpm.Desktop;

internal sealed class DesktopApplication : Avalonia.Application
{
    private static FluentAvaloniaTheme? _fluentTheme;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private bool _exiting;
    private bool _supportsTray;
    private bool _initialized;
    internal static Color AccentColor { get; private set; } = Color.Parse("#5468FF");

    public override void Initialize()
    {
        _fluentTheme = new FluentAvaloniaTheme
        {
            PreferSystemTheme = false,
            PreferUserAccentColor = false,
            UseSystemFontOnWindows = true
        };
        _fluentTheme.CustomAccentColor = AccentColor;
        SetPalette(ThemeVariant.Light, "#5468FF", "#F3F3F3");
        SetPalette(ThemeVariant.Dark, "#8B9CFF", "#202020");
        Styles.Add(_fluentTheme);
    }

    internal static void SetAccent(string accent)
    {
        AccentColor = Color.Parse(accent);
        if (_fluentTheme is not null) _fluentTheme.CustomAccentColor = AccentColor;
    }

    internal static void SetPalette(ThemeVariant variant, string accent, string region)
    {
        if (Current is not { } app) return;
        var accentColor = Color.Parse(accent);
        var light = variant == ThemeVariant.Light;
        var secondaryText = light ? "#5D5D5D" : "#C5C5C5";
        var divider = light ? "#E5E5E5" : "#383838";

        app.Resources.ThemeDictionaries[variant] = new ResourceDictionary
        {
            ["FrpmPrimaryBrush"] = new SolidColorBrush(accentColor),
            ["FrpmWindowBackgroundBrush"] = new SolidColorBrush(Color.Parse(region)),
            ["FrpmDividerBrush"] = new SolidColorBrush(Color.Parse(divider)),
            ["FrpmCardBrush"] = new SolidColorBrush(Color.Parse(light ? "#FFFFFF" : "#2A2A2A")),
            ["FrpmSubtleFillBrush"] = new SolidColorBrush(Color.Parse(light ? "#F2F2F2" : "#333333")),
            ["FrpmSecondaryTextBrush"] = new SolidColorBrush(Color.Parse(secondaryText))
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var workspace = Program.Runtime.Services.GetRequiredService<FrpmWorkspace>();
            var oauth = Program.Runtime.Services.GetRequiredService<DesktopOAuthFlow>();
            _window = new MainWindow(workspace, oauth, Program.DataDirectory, ExitAsync);
            desktop.MainWindow = _window;
            _supportsTray = OperatingSystem.IsWindows() || Program.StartHidden;
            _window.TrayAvailable = _supportsTray;
            desktop.Startup += (_, _) =>
            {
                // Startup fires before Avalonia shows MainWindow. Clearing it here leaves the
                // process and tray running without creating a visible or taskbar window.
                if (Program.StartHidden) desktop.MainWindow = null;
            };
            var menu = new NativeMenu();
            menu.Items.Add(MenuItem("打开 FRPM", () => { ShowWindow(); return Task.CompletedTask; }));
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(MenuItem("启动全部隧道", () => _window.RunActionAsync(() => workspace.SetAllTunnelsRunningAsync(true))));
            menu.Items.Add(MenuItem("停止全部隧道", () => _window.RunActionAsync(() => workspace.SetAllTunnelsRunningAsync(false))));
            menu.Items.Add(MenuItem("打开日志文件夹", () => { DesktopShell.OpenDirectory(Path.Combine(Program.DataDirectory, "logs")); return Task.CompletedTask; }));
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(MenuItem("退出 FRPM", ExitAsync));
            _tray = new TrayIcon { Icon = DesktopShell.Icon(), ToolTipText = "FRPM · 内网穿透管理", Menu = menu };
            _tray.Clicked += (_, _) => ShowWindow();
            TrayIcon.SetIcons(this, new TrayIcons { _tray });
            // OS logoff/shutdown requests must stop the host rather than hide a window.
            desktop.ShutdownRequested += (_, args) => { args.Cancel = true; _ = ExitAsync(); };
            Program.Instance.Listen(() => Dispatcher.UIThread.Post(ShowWindow),
                () => Dispatcher.UIThread.Post(() => _ = ExitAsync()));
            _window.Opened += async (_, _) =>
            {
                if (_initialized) return;
                _initialized = true;
                if (!Program.StartHidden)
                {
                    _supportsTray = await DesktopShell.HasTraySupportAsync();
                    _window.TrayAvailable = _supportsTray;
                }
                await _window.InitializeAsync();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    private NativeMenuItem MenuItem(string title, Func<Task> action)
    {
        var item = new NativeMenuItem(title);
        item.Click += async (_, _) =>
        {
            try { await action(); }
            catch (Exception ex) { if (!_exiting) _window?.ReportError(ex); }
        };
        return item;
    }

    private void ShowWindow()
    {
        if (_window is null || _exiting) return;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        if (_window is not null) _window.AllowClose = true;
        _window?.StopRefreshing();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await Program.Runtime.StopAsync(timeout.Token);
        }
        finally
        {
            _tray?.Dispose();
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
        }
    }
}
