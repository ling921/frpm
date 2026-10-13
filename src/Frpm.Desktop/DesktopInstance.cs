using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Security.Principal;

namespace Frpm.Desktop;

internal sealed class DesktopInstance(FileStream lease, string pipeName) : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private Task? _listener;

    public static bool TryAcquire(string dataDirectory, out DesktopInstance? instance)
    {
        try
        {
            var file = new FileStream(Path.Combine(dataDirectory, "desktop.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            instance = new(file, PipeName(dataDirectory));
            return true;
        }
        catch (IOException) { instance = null; return false; }
    }

    public void Listen(Action activate, Action shutdown) => _listener = Task.Run(async () =>
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                var command = new byte[1];
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                readTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                if (await pipe.ReadAsync(command, readTimeout.Token) == 1)
                {
                    if (command[0] == 1) activate();
                    else if (command[0] == 2)
                    {
                        await pipe.WriteAsync(new byte[] { 1 }, readTimeout.Token);
                        shutdown();
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }
    });

    public static async Task ActivateExistingAsync(string dataDirectory)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName(dataDirectory), PipeDirection.Out,
                ClientOptions());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await pipe.ConnectAsync(timeout.Token);
            if (!IsOurServer(pipe)) throw new IOException("FRPM pipe belongs to a different user.");
            await pipe.WriteAsync(new byte[] { 1 }, timeout.Token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or TimeoutException)
        {
            Console.Error.WriteLine("FRPM 已使用此数据目录运行。后台实例可能尚未准备好或正在无界面模式运行。");
        }
    }

    private static string PipeName(string directory) => "frpm-desktop-" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? directory.ToUpperInvariant() : directory)))[..24];

    public static async Task<bool> StopExistingAsync(string dataDirectory)
    {
        if (TryAcquire(dataDirectory, out var idle)) { idle!.Dispose(); return true; }
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var pipe = new NamedPipeClientStream(".", PipeName(dataDirectory), PipeDirection.InOut,
                ClientOptions());
            await pipe.ConnectAsync(timeout.Token);
            if (!IsOurServer(pipe)) throw new IOException("FRPM pipe belongs to a different user.");
            await pipe.WriteAsync(new byte[] { 2 }, timeout.Token);
            var reply = new byte[1];
            if (await pipe.ReadAsync(reply, timeout.Token) != 1 || reply[0] != 1) return false;
            while (!timeout.IsCancellationRequested)
            {
                if (TryAcquire(dataDirectory, out var released)) { released!.Dispose(); return true; }
                await Task.Delay(200, timeout.Token);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or TimeoutException)
        {
            Console.Error.WriteLine("FRPM 未能完成退出，请手动退出后重试。");
        }
        return false;
    }

    private static PipeOptions ClientOptions() => PipeOptions.Asynchronous
        | (OperatingSystem.IsWindows() ? PipeOptions.None : PipeOptions.CurrentUserOnly);

    private static bool IsOurServer(NamedPipeClientStream pipe)
    {
        if (!OperatingSystem.IsWindows()) return true;
        // Installer/uninstaller shutdown requests may cross UAC elevation for the same user.
        // The server's current-user ACL remains in place; validate its owner explicitly.
        using var identity = WindowsIdentity.GetCurrent();
        var owner = pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
        return owner is not null && (owner.Equals(identity.User) || owner.Equals(identity.Owner));
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener?.GetAwaiter().GetResult();
        _stop.Dispose();
        lease.Dispose();
    }
}
