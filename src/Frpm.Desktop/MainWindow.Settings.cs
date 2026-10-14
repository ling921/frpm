using Avalonia.Controls;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using Frpm.Abstractions.Models;

namespace Frpm.Desktop;

internal sealed partial class MainWindow
{
    private async Task RenderSettingsAsync()
    {
        var settings = await _workspace.GetSettingsAsync(_lifetime.Token);
        var preferences = await _workspace.GetPreferencesAsync(_lifetime.Token);
        var accounts = await _workspace.GetAccountsAsync(_lifetime.Token);
        BeginPage("设置", "调整同步与外观，查看本机运行环境");
        var interval = new NumericUpDown { Minimum = 15, Maximum = 3600, Value = settings.ProviderSyncIntervalSeconds,
            FormatString = "0", Width = 320, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        var syncActions = new List<Control>
        {
            ActionButton("保存同步设置", async () => await _workspace.SaveSettingsAsync((int)(interval.Value ?? 60), _lifetime.Token), primary: true)
        };
        if (accounts.Any(account => account.Enabled))
            syncActions.Add(ActionButton("立即同步", () => _workspace.SyncAllAsync(_lifetime.Token)));
        PageContent.Children.Add(Section(Stack(Text("供应商同步", size: 18), SettingField("自动同步间隔（秒）", interval),
            Actions(syncActions.ToArray()))));
        var startup = new DesktopStartupManager();
        var startupEnabled = false;
        string? startupError = null;
        try { startupEnabled = startup.IsSupported && await startup.IsEnabledAsync(); }
        catch (Exception ex) { startupError = ex.GetBaseException().Message; }
        var startupToggle = new ToggleSwitch
        {
            OnContent = "开",
            OffContent = "关",
            IsChecked = startupEnabled,
            IsEnabled = startup.IsSupported && startupError is null,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };
        var updatingStartup = false;
        async Task ApplyStartupAsync(bool requested)
        {
            if (updatingStartup) return;
            updatingStartup = true;
            startupToggle.IsEnabled = false;
            try
            {
                await startup.SetEnabledAsync(requested);
                var actual = await startup.IsEnabledAsync();
                if (actual != requested)
                    throw new InvalidOperationException("系统未能应用启动设置，请检查系统的启动应用权限。");
                startupEnabled = actual;
                ShowStatus(actual ? "已启用登录时启动 FRPM。" : "已关闭登录时启动 FRPM。", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                startupToggle.IsChecked = startupEnabled;
                ShowStatus($"无法更改登录启动设置：{ex.GetBaseException().Message}", InfoBarSeverity.Error);
            }
            finally
            {
                startupToggle.IsEnabled = startup.IsSupported;
                updatingStartup = false;
            }
        }
        startupToggle.IsCheckedChanged += async (_, _) => await ApplyStartupAsync(startupToggle.IsChecked == true);
        PageContent.Children.Add(Section(Stack(Text("启动", size: 18),
            SettingField("登录时启动", startupToggle),
            Text(startupError is not null ? $"无法读取系统启动设置：{startupError}。请刷新后重试。" : startup.IsSupported
                ? "启用后，FRPM 会在当前用户登录时启动；支持系统托盘时会在后台运行。"
                : "当前平台暂不支持设置登录启动项。", hint: true))));
        var mode = Choice(new[] { "auto", "light", "dark" }, preferences.ThemeMode,
            value => value switch { "light" => "浅色", "dark" => "深色", _ => "跟随系统" });
        mode.Width = 320;
        mode.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        var lightPrimary = ColorPicker(preferences.ThemeLightPrimaryColor);
        var lightSecondary = ColorPicker(preferences.ThemeLightSecondaryColor);
        var darkPrimary = ColorPicker(preferences.ThemeDarkPrimaryColor);
        var darkSecondary = ColorPicker(preferences.ThemeDarkSecondaryColor);
        PageContent.Children.Add(Section(Stack(Text("外观", size: 18), SettingField("主题", mode),
            SettingField("浅色主题主色", lightPrimary),
            SettingField("浅色主题辅助色", lightSecondary),
            SettingField("深色主题主色", darkPrimary),
            SettingField("深色主题辅助色", darkSecondary),
            ActionButton("保存外观", async () =>
            {
                var saved = await _workspace.SavePreferencesAsync(new(mode.SelectedItem as string ?? "auto",
                    HexColor(lightPrimary.Color), HexColor(lightSecondary.Color),
                    HexColor(darkPrimary.Color), HexColor(darkSecondary.Color)), _lifetime.Token);
                ApplyTheme(saved);
            }, primary: true))));
        PageContent.Children.Add(Section(Stack(Text("运行环境", size: 18),
            Text($"FRPM {settings.ApplicationVersion} · {settings.FrameworkDescription}"),
            Text($"{settings.OperatingSystemDescription} · {settings.Architecture}"),
            Text($"本次启动：{Time(settings.LastStartedAt)} · 首次启动：{Time(settings.FirstStartedAt)}", hint: true),
            Text($"最近同步：{Time(settings.LastProviderSyncAt)} · 下次同步：{Time(settings.NextProviderSyncAt)}", hint: true),
            Text($"数据目录：{_dataDirectory}", hint: true),
            Text(TrayAvailable ? "关闭窗口会隐藏到系统托盘，隧道继续运行。退出 FRPM 或注销用户会停止隧道；下次启动会恢复。"
                : "当前桌面未提供托盘，关闭窗口会询问是否退出。退出后隧道停止，下次启动会恢复。", hint: true),
                Actions(ActionButton("打开数据目录", () => { DesktopShell.OpenDirectory(_dataDirectory); return Task.CompletedTask; }),
                ActionButton("打开日志目录", () => { DesktopShell.OpenDirectory(Path.Combine(_dataDirectory, "logs")); return Task.CompletedTask; }),
                ActionButton("复制诊断信息", async () =>
                {
                    if (Clipboard is null) throw new InvalidOperationException("当前环境无法访问剪贴板。");
                    await Clipboard.SetTextAsync($"{Title}\n{settings.FrameworkDescription}\n{settings.OperatingSystemDescription} · {settings.Architecture}\n数据目录：{_dataDirectory}\n托盘可用：{TrayAvailable}");
                    ShowStatus("诊断信息已复制，可粘贴到问题反馈中；不包含账号凭据或隧道配置。", InfoBarSeverity.Success);
                }),
                ActionButton("反馈问题", () => { DesktopShell.OpenUrl("https://github.com/ling921/frpm/issues"); return Task.CompletedTask; })))));
    }

    private static ColorPicker ColorPicker(string color) => new()
    {
        Color = Color.Parse(color),
        IsAlphaEnabled = false,
        IsColorSpectrumVisible = true,
        IsColorPaletteVisible = true,
        IsColorComponentsVisible = true,
        IsHexInputVisible = true,
        Width = 148,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
    };

    private static Control SettingField(string label, Control input)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*") };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        input.Margin = new Avalonia.Thickness(12, 0, 0, 0);
        Grid.SetColumn(input, 1);
        row.Children.Add(input);
        return row;
    }

    private static string HexColor(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
