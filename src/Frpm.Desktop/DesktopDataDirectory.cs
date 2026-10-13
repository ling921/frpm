using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Frpm.Desktop;

internal static class DesktopDataDirectory
{
    internal static string ResolvePhysicalPath(string directory)
    {
        var fullPath = Path.GetFullPath(directory);
        if (!OperatingSystem.IsWindows()) return fullPath;

        // Opening the directory applies MSIX redirection; querying the handle then
        // returns its real location. This also keeps existing packaged data in place
        // and supports --data-dir without guessing the package family or cache layout.
        using var handle = CreateFileW(fullPath, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var buffer = new StringBuilder(512);
        while (true)
        {
            var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
            if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (length < buffer.Capacity) break;
            buffer.EnsureCapacity(checked((int)length + 1));
        }

        // CLI programs need conventional DOS/UNC paths rather than Win32 device paths.
        var path = buffer.ToString();
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return @"\\" + path[8..];
        return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle,
        StringBuilder path, uint length, uint flags);
}
