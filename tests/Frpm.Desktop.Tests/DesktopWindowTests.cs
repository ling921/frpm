using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using FluentAvalonia.Styling;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Frpm.Application;
using Frpm.Data;
using Frpm.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

[assembly: AvaloniaTestApplication(typeof(Frpm.Desktop.Tests.TestAppBuilder))]

namespace Frpm.Desktop.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApplication>()
        .WithInterFont().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class TestApplication : Avalonia.Application
{
    public override void Initialize() => Styles.Add(new FluentAvaloniaTheme
    {
        PreferSystemTheme = false,
        PreferUserAccentColor = false
    });
}

public sealed class DesktopWindowTests
{
    [AvaloniaFact]
    public async Task Closing_to_tray_keeps_runtime_alive_and_reopening_restores_window()
    {
        await using var runtime = new TestRuntime();
        await runtime.StartAsync();
        var exits = 0;
        var window = new MainWindow(runtime.Workspace, runtime.Host.Services.GetRequiredService<DesktopOAuthFlow>(), runtime.Directory,
            () => { exits++; return Task.CompletedTask; }) { TrayAvailable = true };
        try
        {
            window.Show();
            await window.InitializeAsync();
            window.Close();
            Assert.False(window.IsVisible);
            Assert.Equal(0, exits);
            Assert.Equal(0, runtime.Supervisor.Shutdowns);
            Assert.Equal(0, (await runtime.Workspace.GetDashboardAsync()).TunnelCount);
            window.Show();
            Assert.True(window.IsVisible);
        }
        finally { window.AllowClose = true; window.StopRefreshing(); window.Close(); }
        await runtime.Host.StopAsync();
        Assert.True(runtime.Supervisor.PreservedDesiredState);
    }

    [AvaloniaFact]
    public async Task All_native_pages_load_against_shared_services_without_web_host()
    {
        await using var runtime = new TestRuntime();
        await runtime.StartAsync(seed: true);
        var window = new MainWindow(runtime.Workspace, runtime.Host.Services.GetRequiredService<DesktopOAuthFlow>(), runtime.Directory, () => Task.CompletedTask)
            { TrayAvailable = true };
        try
        {
            window.Show();
            await window.InitializeAsync();
            var navigation = window.FindControl<NavigationView>("Navigation")!;
            var title = window.FindControl<TextBlock>("PageTitle")!;
            var busy = window.FindControl<ProgressBar>("BusyBar")!;
            var pages = new[] { "概览", "供应商账号", "隧道", "CLI 版本", "运行日志", "设置" };
            for (var index = 0; index < pages.Length; index++)
            {
                var navigationItem = window.GetVisualDescendants().OfType<NavigationViewItem>()
                    .Single(item => item.Content?.ToString() == pages[index]);
                navigation.SelectedItem = navigationItem;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                while (busy.IsVisible || title.Text != pages[index]) await Task.Delay(10, timeout.Token);
                var status = window.FindControl<TextBlock>("StatusMessage")!.Text ?? "";
                Assert.DoesNotContain("操作失败", status);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == pages[index]);
                var buttonLabels = window.GetVisualDescendants().OfType<Button>()
                    .Select(button => button.Content?.ToString()).Where(label => label is not null).ToArray();
                if (pages[index] == "供应商账号")
                    Assert.DoesNotContain("同步全部", buttonLabels);
                if (pages[index] == "隧道")
                {
                    Assert.Equal(2, buttonLabels.Count(label => label == "启动"));
                    Assert.DoesNotContain("停止", buttonLabels);
                }
                if (pages[index] == "CLI 版本")
                {
                    Assert.DoesNotContain("设为活动版本", buttonLabels);
                    Assert.DoesNotContain("删除版本", buttonLabels);
                }
                if (pages[index] == "运行日志")
                    Assert.DoesNotContain("停止隧道", buttonLabels);
                if (Environment.GetEnvironmentVariable("FRPM_UI_ARTIFACT_DIRECTORY") is { Length: > 0 } output)
                {
                    System.IO.Directory.CreateDirectory(output);
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    frame.Save(Path.Combine(output, $"desktop-page-{index}.png"));
                }
            }
            // Change the native settings controls, then execute their actual save handler.
            var mode = window.GetVisualDescendants().OfType<ComboBox>().First();
            mode.SelectedItem = "dark";
            var save = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "保存外观");
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while (busy.IsVisible) await Task.Delay(10);
            Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
            Assert.Equal("dark", (await runtime.Workspace.GetPreferencesAsync()).ThemeMode);
            if (Environment.GetEnvironmentVariable("FRPM_UI_ARTIFACT_DIRECTORY") is { Length: > 0 } darkOutput)
            {
                using var darkFrame = window.CaptureRenderedFrame();
                darkFrame!.Save(Path.Combine(darkOutput, "desktop-dark.png"));
            }
        }
        finally { window.AllowClose = true; window.StopRefreshing(); window.Close(); }
    }

    [Fact]
    public async Task Local_preferences_and_database_migrations_work_without_blazor_authentication()
    {
        await using var runtime = new TestRuntime();
        await runtime.StartAsync();
        var prefs = await runtime.Workspace.SavePreferencesAsync(new("dark", "#5468ff", "#52606d", "#8b9cff", "#a8b3cf"));
        Assert.Equal("dark", prefs.ThemeMode);
        Assert.Equal(prefs, await runtime.Workspace.GetPreferencesAsync());
        Assert.Equal(1, runtime.Supervisor.Restores);
        Assert.True(File.Exists(Path.Combine(runtime.Directory, "frpm.db")));
        Assert.DoesNotContain(runtime.Host.GetType().Assembly.GetReferencedAssemblies(), a => a.Name?.Contains("Blazor") == true);
    }

    [Fact]
    public void Random_remote_ports_use_high_range_and_skip_ports_already_in_use()
    {
        var usedPorts = Enumerable.Range(20_000, 100).ToHashSet();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var port = MainWindow.RandomRemotePort(usedPorts);
            Assert.InRange(port, 20_000, 60_000);
            Assert.True(usedPorts.Add(port));
        }
    }

    [Fact]
    public void Random_remote_ports_follow_the_selected_nodes_port_range()
    {
        var usedPorts = Enumerable.Range(40_000, 10).ToHashSet();
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var port = MainWindow.RandomRemotePort(usedPorts, "40000-40100");
            Assert.InRange(port, 40_000, 40_100);
            Assert.True(usedPorts.Add(port));
        }
    }

    [Fact]
    public async Task Linux_startup_setting_adds_a_background_entry_and_can_remove_it()
    {
        var root = Path.Combine(Path.GetTempPath(), $"frpm-startup-{Guid.NewGuid():N}");
        var executable = Path.Combine(root, "FRPM Client", "Frpm Desktop");
        var autostartDirectory = Path.Combine(root, "config", "autostart");
        var startup = new DesktopStartupManager(executable, autostartDirectory, DesktopStartupPlatform.Linux);
        try
        {
            Assert.False(await startup.IsEnabledAsync());
            await startup.SetEnabledAsync(true);
            Assert.True(await startup.IsEnabledAsync());
            var entry = await File.ReadAllTextAsync(Path.Combine(autostartDirectory, "frpm.desktop"));
            Assert.StartsWith("[Desktop Entry]", entry);
            Assert.Contains("FRPM Client", entry);
            Assert.Contains(" --background", entry);

            await startup.SetEnabledAsync(false);
            Assert.False(await startup.IsEnabledAsync());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Bulk_tunnel_actions_skip_tunnels_already_in_requested_state()
    {
        await using var runtime = new TestRuntime();
        await runtime.StartAsync(seed: true);
        var factory = runtime.Host.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var tunnel = await db.Tunnels.OrderBy(item => item.Name).FirstAsync();
            tunnel.RuntimeState = TunnelRuntimeState.Running;
            await db.SaveChangesAsync();
        }
        await runtime.Workspace.SetAllTunnelsRunningAsync(true);
        Assert.Equal(1, runtime.Supervisor.Starts);
        await runtime.Workspace.SetAllTunnelsRunningAsync(false);
        Assert.Equal(1, runtime.Supervisor.Stops);
    }

    [AvaloniaFact]
    public async Task Tunnel_rows_show_start_or_stop_based_on_runtime_state()
    {
        await using var runtime = new TestRuntime();
        await runtime.StartAsync(seed: true);
        var factory = runtime.Host.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var tunnel = await db.Tunnels.OrderBy(item => item.Name).FirstAsync();
            tunnel.RuntimeState = TunnelRuntimeState.Running;
            await db.SaveChangesAsync();
        }
        var window = new MainWindow(runtime.Workspace,
            runtime.Host.Services.GetRequiredService<DesktopOAuthFlow>(), runtime.Directory, () => Task.CompletedTask)
        { TrayAvailable = true };
        try
        {
            window.Show();
            await window.InitializeAsync();
            window.FindControl<NavigationView>("Navigation")!.SelectedItem = window.FindControl<NavigationViewItem>("TunnelsNavigationItem");
            var busy = window.FindControl<ProgressBar>("BusyBar")!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (busy.IsVisible || window.FindControl<TextBlock>("PageTitle")!.Text != "隧道")
                await Task.Delay(10, timeout.Token);
            var labels = window.GetVisualDescendants().OfType<Button>().Select(button => button.Content?.ToString()).ToArray();
            Assert.Equal(1, labels.Count(label => label == "启动"));
            Assert.Equal(1, labels.Count(label => label == "停止"));
        }
        finally { window.AllowClose = true; window.StopRefreshing(); window.Close(); }
    }
}
