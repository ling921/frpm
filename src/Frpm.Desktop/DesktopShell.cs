using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Frpm.Desktop;

internal static class DesktopShell
{
    public static WindowIcon Icon() => new(AssetLoader.Open(new Uri("avares://Frpm.Desktop/Assets/frpm.ico")));
    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    public static void OpenDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        OpenUrl(directory);
    }

    public static async Task<bool> HasTraySupportAsync()
    {
        if (OperatingSystem.IsWindows()) return true;
        if (!OperatingSystem.IsLinux()) return false;
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("dbus-send")
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                    ArgumentList = { "--session", "--print-reply", "--dest=org.freedesktop.DBus", "/org/freedesktop/DBus",
                        "org.freedesktop.DBus.NameHasOwner", "string:org.kde.StatusNotifierWatcher" }
                }
            };
            process.Start();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                return process.ExitCode == 0 && output.Contains("boolean true", StringComparison.Ordinal);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill();
                return false;
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { return false; }
    }
}
