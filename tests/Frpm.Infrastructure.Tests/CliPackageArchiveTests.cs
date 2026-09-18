using Frpm.Domain.Enums;
using Frpm.Infrastructure.Runtime;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Frpm.Infrastructure.Tests;

public sealed class CliPackageArchiveTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "frpm-cli-tests", Guid.NewGuid().ToString("N"));

    public CliPackageArchiveTests() => Directory.CreateDirectory(_testDirectory);

    [Fact]
    public void ExtractZip_FindsCurrentPlatformExecutableInNestedDirectory()
    {
        var executableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "frpc.exe" : "frpc";
        var archivePath = CreateZip(("LoliaFrp/bin/" + executableName, "test executable"));
        var destination = Path.Combine(_testDirectory, "content");

        CliPackageManager.ExtractZip(archivePath, destination);

        var executable = CliPackageManager.FindExecutable(destination, ProviderType.LoliaFrp);
        Assert.Equal(executableName, Path.GetFileName(executable), ignoreCase: true);
        Assert.Equal("test executable", File.ReadAllText(executable));
    }

    [Fact]
    public void FindExecutable_RejectsBinaryForAnotherPlatform()
    {
        var wrongName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "frpc" : "frpc.exe";
        var content = Path.Combine(_testDirectory, "wrong-platform");
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(content, wrongName), "wrong platform");

        var error = Assert.Throws<InvalidOperationException>(() => CliPackageManager.FindExecutable(content, ProviderType.LoliaFrp));

        Assert.Contains(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "frpc.exe" : "frpc", error.Message);
    }

    [Fact]
    public void FindExecutable_FindsMeFrpExecutableName()
    {
        var extension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";
        var content = Path.Combine(_testDirectory, "mefrp");
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(content, $"mefrpc{extension}"), "ME Frp executable");

        var executable = CliPackageManager.FindExecutable(content, ProviderType.MeFrp);

        Assert.Equal($"mefrpc{extension}", Path.GetFileName(executable), ignoreCase: true);
    }

    [Fact]
    public void FindExecutable_PrefersProviderExecutableOverGenericSuffixMatch()
    {
        var extension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";
        var content = Path.Combine(_testDirectory, "preferred");
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(content, $"customfrpc{extension}"), "fallback executable");
        File.WriteAllText(Path.Combine(content, $"mefrpc{extension}"), "preferred executable");

        var executable = CliPackageManager.FindExecutable(content, ProviderType.MeFrp);

        Assert.Equal($"mefrpc{extension}", Path.GetFileName(executable), ignoreCase: true);
    }

    [Fact]
    public void ExtractZip_RejectsParentDirectoryTraversal()
    {
        var archivePath = CreateZip(("../escaped", "unsafe"));
        var destination = Path.Combine(_testDirectory, "safe-content");

        Assert.Throws<InvalidOperationException>(() => CliPackageManager.ExtractZip(archivePath, destination));
        Assert.False(File.Exists(Path.Combine(_testDirectory, "escaped")));
    }

    [Fact]
    public async Task VerifyExecutable_AcceptsCurrentPlatformBinaryHeader()
    {
        var executable = Path.Combine(_testDirectory, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "frpc.exe" : "frpc");
        var header = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new byte[] { (byte)'M', (byte)'Z', 0, 0 }
            : new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' };
        await File.WriteAllBytesAsync(executable, header);

        await CliPackageManager.VerifyExecutableAsync(executable, CancellationToken.None);
    }

    [Fact]
    public async Task VerifyExecutable_RejectsInvalidBinaryHeader()
    {
        var executable = Path.Combine(_testDirectory, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "frpc.exe" : "frpc");
        await File.WriteAllTextAsync(executable, "not an executable", CancellationToken.None);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CliPackageManager.VerifyExecutableAsync(executable, CancellationToken.None));

        Assert.Contains(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows PE" : "Linux ELF", error.Message);
    }

    [Fact]
    public async Task DetectPackageContent_PrefersExecutableHeaderOverMisleadingExtension()
    {
        var path = Path.Combine(_testDirectory, "frpc.zip");
        var header = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new byte[] { (byte)'M', (byte)'Z', 0, 0 }
            : new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' };
        await File.WriteAllBytesAsync(path, header);

        var kind = CliPackageManager.DetectPackageContent(path);

        Assert.Equal(CliPackageContentKind.Executable, kind);
    }

    [Fact]
    public void DetectPackageContent_RecognizesZipHeaderWithExeExtension()
    {
        var archivePath = CreateZip(("frpc", "content"));
        var misleadingPath = Path.Combine(_testDirectory, "download.exe");
        File.Move(archivePath, misleadingPath);

        var kind = CliPackageManager.DetectPackageContent(misleadingPath);

        Assert.Equal(CliPackageContentKind.Zip, kind);
    }

    [Fact]
    public void ExtractTar_FindsCurrentPlatformExecutable()
    {
        var executableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "frpc.exe" : "frpc";
        var archivePath = Path.Combine(_testDirectory, "vendor-package.tar");
        using (var stream = File.Create(archivePath))
        using (var writer = new TarWriter(stream, leaveOpen: false))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, $"vendor/bin/{executableName}")
            {
                DataStream = new MemoryStream("tar executable"u8.ToArray())
            };
            writer.WriteEntry(entry);
        }
        var destination = Path.Combine(_testDirectory, "tar-content");

        Assert.Equal(CliPackageContentKind.Tar, CliPackageManager.DetectPackageContent(archivePath));
        CliPackageManager.ExtractTar(archivePath, destination);

        var executable = CliPackageManager.FindExecutable(destination, ProviderType.LoliaFrp);
        Assert.Equal("tar executable", File.ReadAllText(executable));
    }

    private string CreateZip(params (string Name, string Content)[] entries)
    {
        var archivePath = Path.Combine(_testDirectory, $"{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (var item in entries)
        {
            var entry = archive.CreateEntry(item.Name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(item.Content);
        }

        return archivePath;
    }

    public void Dispose()
    {
        var resolved = Path.GetFullPath(_testDirectory);
        var expectedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "frpm-cli-tests")) + Path.DirectorySeparatorChar;
        if (resolved.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved))
        {
            Directory.Delete(resolved, true);
        }
    }
}
