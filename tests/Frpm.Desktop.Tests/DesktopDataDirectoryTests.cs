namespace Frpm.Desktop.Tests;

public sealed class DesktopDataDirectoryTests
{
    [Fact]
    public void Physical_directory_can_be_used_by_external_processes()
    {
        var path = DesktopDataDirectory.ResolvePhysicalPath(Path.GetTempPath());
        Assert.True(Path.IsPathFullyQualified(path));
        Assert.True(Directory.Exists(path));
        Assert.False(path.StartsWith(@"\\?\", StringComparison.Ordinal));
    }

    [Fact]
    public void Windows_resolves_a_directory_link_to_its_target()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), $"frpm-path-{Guid.NewGuid():N}");
        var target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
        var link = Path.Combine(root, "link");
        try
        {
            // Junctions do not require the developer-mode privilege needed by symlinks.
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            })!;
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
            Assert.Equal(DesktopDataDirectory.ResolvePhysicalPath(target),
                DesktopDataDirectory.ResolvePhysicalPath(link));
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(target);
            Directory.Delete(root);
        }
    }
}
