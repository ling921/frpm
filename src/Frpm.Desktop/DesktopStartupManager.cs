using System.Text;
using Microsoft.Win32;
#if WINDOWS
using System.Runtime.InteropServices;
using Windows.ApplicationModel;
#endif

namespace Frpm.Desktop;

internal enum DesktopStartupPlatform
{
    Unsupported,
    Windows,
    Linux
}

internal sealed class DesktopStartupManager
{
    private const string StartupTaskId = "FRPMStartup";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "FRPM";
    private readonly string _executablePath;
    private readonly string _autostartDirectory;
    private readonly DesktopStartupPlatform _platform;

    public DesktopStartupManager()
        : this(Environment.ProcessPath ?? throw new InvalidOperationException("无法确定 FRPM 程序路径。"))
    {
    }

    internal DesktopStartupManager(
        string executablePath,
        string? autostartDirectory = null,
        DesktopStartupPlatform? platform = null)
    {
        _executablePath = Path.GetFullPath(executablePath);
        _autostartDirectory = autostartDirectory ?? GetAutostartDirectory();
        _platform = platform ?? GetCurrentPlatform();
    }

    public bool IsSupported => _platform is DesktopStartupPlatform.Windows or DesktopStartupPlatform.Linux;

    public async Task<bool> IsEnabledAsync()
    {
        return _platform switch
        {
            DesktopStartupPlatform.Windows => await IsWindowsStartupEnabledAsync(),
            DesktopStartupPlatform.Linux => File.Exists(AutostartFilePath),
            _ => false
        };
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        switch (_platform)
        {
            case DesktopStartupPlatform.Windows:
                await SetWindowsStartupEnabledAsync(enabled);
                break;
            case DesktopStartupPlatform.Linux:
                SetLinuxStartupEnabled(enabled);
                break;
            default:
                throw new PlatformNotSupportedException("当前平台暂不支持登录时自动启动。");
        }
    }

    private string AutostartFilePath => Path.Combine(_autostartDirectory, "frpm.desktop");

    private async Task<bool> IsWindowsStartupEnabledAsync()
    {
        if (!OperatingSystem.IsWindows()) return false;
#if WINDOWS
        if (IsPackagedWindowsApplication())
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            using var legacyKey = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy
                || legacyKey?.GetValue(RunValueName) is string || HasLegacyMachineStartup();
        }
#endif
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is string || HasLegacyMachineStartup();
    }

    private async Task SetWindowsStartupEnabledAsync(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("当前平台暂不支持 Windows 登录启动项。");
#if WINDOWS
        if (IsPackagedWindowsApplication())
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            if (!enabled)
            {
                task.Disable();
                // A previous unpackaged installation can leave a second startup entry.
                using var legacyKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                legacyKey?.DeleteValue(RunValueName, throwOnMissingValue: false);
                CheckLegacyMachineStartup();
                if (task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy)
                    throw new InvalidOperationException("Windows 未能禁用启动任务，可能受系统策略限制。请检查“设置 > 应用 > 启动”。");
                return;
            }

            var state = await task.RequestEnableAsync();
            if (state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy) return;
            throw new InvalidOperationException(state switch
            {
                StartupTaskState.DisabledByUser => "Windows 已禁止 FRPM 登录时启动。请在“设置 > 应用 > 启动”中重新启用 FRPM。",
                StartupTaskState.DisabledByPolicy => "此设备的策略禁止 FRPM 登录时启动。",
                _ => "Windows 未能启用 FRPM 登录时启动，请检查系统启动应用设置。"
            });
        }
#endif

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开当前用户的启动注册项。");
        if (enabled)
            key.SetValue(RunValueName, $"\"{_executablePath}\" --background", RegistryValueKind.String);
        else
        {
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
            CheckLegacyMachineStartup();
        }
    }

    private static bool HasLegacyMachineStartup()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var key = Registry.LocalMachine.OpenSubKey(RunKeyPath);
        return key?.GetValue("FRPM Tray") is string;
    }

    private static void CheckLegacyMachineStartup()
    {
        if (HasLegacyMachineStartup())
            throw new InvalidOperationException("当前版本启动项已关闭，但旧版 Windows 安装程序留下了系统级“FRPM Tray”启动项。请在 Windows 启动应用中禁用它，或卸载旧版安装程序。");
    }

    private void SetLinuxStartupEnabled(bool enabled)
    {
        if (!enabled)
        {
            File.Delete(AutostartFilePath);
            return;
        }

        Directory.CreateDirectory(_autostartDirectory);
        var executable = EscapeDesktopExecArgument(_executablePath);
        var content = string.Join(Environment.NewLine,
            "[Desktop Entry]",
            "Type=Application",
            "Name=FRPM",
            "Comment=内网穿透管理",
            $"Exec=\"{executable}\" --background",
            "Terminal=false",
            "X-GNOME-Autostart-enabled=true",
            string.Empty);
        File.WriteAllText(AutostartFilePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string EscapeDesktopExecArgument(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("%", "%%", StringComparison.Ordinal);

    private static string GetAutostartDirectory()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome) || !Path.IsPathRooted(configHome))
            configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(configHome, "autostart");
    }

    private static DesktopStartupPlatform GetCurrentPlatform() => OperatingSystem.IsWindows()
        ? DesktopStartupPlatform.Windows
        : OperatingSystem.IsLinux() ? DesktopStartupPlatform.Linux : DesktopStartupPlatform.Unsupported;

#if WINDOWS
    private static bool IsPackagedWindowsApplication()
    {
        try
        {
            _ = Package.Current.Id;
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return false;
        }
    }
#endif
}
