using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Frpm.Tray;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    private static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TrayApplication>()
        .UsePlatformDetect()
        .LogToTrace();
}
