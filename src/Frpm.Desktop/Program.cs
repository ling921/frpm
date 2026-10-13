using Avalonia;
using Avalonia.Controls;
using Frpm.Abstractions.Services;
using Frpm.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Frpm.Desktop;

internal static class Program
{
    internal const string LocalUserId = "frpm-desktop-user";
    internal static IHost Runtime { get; private set; } = null!;
    internal static DesktopInstance Instance { get; private set; } = null!;
    internal static string DataDirectory { get; private set; } = string.Empty;
    internal static bool StartHidden { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        var headless = args.Contains("--headless");
        StartHidden = args.Contains("--background")
            || string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "Frpm.Startup", StringComparison.OrdinalIgnoreCase);
        var dataIndex = Array.IndexOf(args, "--data-dir");
        var importIndex = Array.IndexOf(args, "--import-data");
        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine("FRPM Desktop: [--background] [--headless] [--shutdown] [--data-dir DIRECTORY] [--import-data OLD_DATA_DIRECTORY]");
            return 0;
        }
        if (dataIndex >= 0 && (dataIndex + 1 >= args.Length || args[dataIndex + 1].StartsWith("--")))
        {
            Console.Error.WriteLine("--data-dir requires a directory.");
            return 1;
        }
        if (importIndex >= 0 && (importIndex + 1 >= args.Length || args[importIndex + 1].StartsWith("--")))
        {
            Console.Error.WriteLine("--import-data requires the old data directory.");
            return 1;
        }
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] is "--data-dir" or "--import-data") { index++; continue; }
            if (args[index] is "--headless" or "--background" or "--shutdown") continue;
            Console.Error.WriteLine($"未知参数：{args[index]}。使用 --help 查看原生桌面程序支持的参数。");
            return 1;
        }
        DataDirectory = dataIndex >= 0 ? Path.GetFullPath(args[dataIndex + 1]) : DefaultDataDirectory();
        Directory.CreateDirectory(DataDirectory);
        // MSIX can redirect AppData writes. External CLI processes need the physical
        // directory, rather than the virtual path visible only to the packaged app.
        DataDirectory = DesktopDataDirectory.ResolvePhysicalPath(DataDirectory);
        if (args.Contains("--shutdown")) return DesktopInstance.StopExistingAsync(DataDirectory).GetAwaiter().GetResult() ? 0 : 1;
        if (!DesktopInstance.TryAcquire(DataDirectory, out var instance))
        {
            if (headless || importIndex >= 0)
            {
                Console.Error.WriteLine("此数据目录正在被另一个 FRPM 实例使用，请先退出该实例。");
                return 1;
            }
            if (!StartHidden) DesktopInstance.ActivateExistingAsync(DataDirectory).GetAwaiter().GetResult();
            return 0;
        }
        using (Instance = instance!)
        {
            try
            {
                // Keep a background launch completely hidden only when this session has a tray
                // through which the user can reopen the window or exit the application.
                if (!headless && StartHidden)
                    StartHidden = DesktopShell.HasTraySupportAsync().GetAwaiter().GetResult();

                if (importIndex >= 0)
                {
                    FrpmDataImporter.ImportAsync(args[importIndex + 1], DataDirectory).GetAwaiter().GetResult();
                    Console.WriteLine($"旧版 FRPM 数据已导入：{DataDirectory}");
                    return 0;
                }
                var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
                {
                    ContentRootPath = AppContext.BaseDirectory, Args = []
                });
                builder.Configuration.AddJsonFile(Path.Combine(DataDirectory, "appsettings.json"), optional: true)
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Frpm:Storage:DataDirectory"] = DataDirectory,
                        ["ConnectionStrings:DefaultConnection"] = $"Data Source={Path.Combine(DataDirectory, "frpm.db")}" 
                    });
                builder.Logging.AddProvider(new DesktopFileLoggerProvider(Path.Combine(DataDirectory, "logs")));
                builder.Services.AddFrpmApplication(builder.Configuration);
                builder.Services.AddSingleton<ICurrentUserIdentity, DesktopUserIdentity>();
                using (Runtime = builder.Build())
                {
                    Runtime.Services.InitializeFrpmDatabaseAsync(LocalUserId).GetAwaiter().GetResult();
                    if (headless)
                    {
                        Runtime.RunAsync().GetAwaiter().GetResult();
                    }
                    else
                    {
                        Runtime.StartAsync().GetAwaiter().GetResult();
                        try
                        {
                            AppBuilder.Configure<DesktopApplication>().UsePlatformDetect().WithInterFont()
                                .LogToTrace().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
                        }
                        finally
                        {
                            using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                            Runtime.StopAsync(shutdownTimeout.Token).GetAwaiter().GetResult();
                        }
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FRPM 启动失败：{ex.Message}");
                File.AppendAllText(Path.Combine(DataDirectory, "startup-error.log"), $"{DateTimeOffset.Now:O} {ex}{Environment.NewLine}");
                return 1;
            }
        }
    }

    private static string DefaultDataDirectory()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FRPM", "data");
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var root = !string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg)
            ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(root, "frpm", "data");
    }

    private sealed class DesktopUserIdentity : ICurrentUserIdentity
    {
        public Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(LocalUserId);
    }
}
