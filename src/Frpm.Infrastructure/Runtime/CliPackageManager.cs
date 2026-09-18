using Frpm.Data;
using Frpm.Domain.Entities;
using Frpm.Domain.Enums;
using Frpm.Infrastructure.Options;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Frpm.Infrastructure.Runtime;

[SingletonService(typeof(ICliPackageManager))]
public sealed class CliPackageManager(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IHttpClientFactory httpClientFactory,
    ITunnelProcessSupervisor supervisor,
    IOptions<FrpmStorageOptions> options) : ICliPackageManager
{
    private readonly FrpmStorageOptions _options = options.Value;
    private readonly string _dataDirectory = Path.GetFullPath(options.Value.DataDirectory);

    public async Task<CliPackage> InstallFromUrlAsync(
        ProviderType providerType,
        string version,
        string url,
        string? expectedSha256,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("只允许 HTTP/HTTPS 下载地址。");

        try
        {
            using var response = await httpClientFactory.CreateClient("cli-download").GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > _options.MaxPackageBytes) throw new InvalidOperationException("CLI 包超过大小限制。");
            var fileName = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "frpc";
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var package = await InstallFromStreamAsync(providerType, version, fileName, stream, expectedSha256, replaceExisting, cancellationToken);
            package.SourceUrl = url;
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            db.CliPackages.Update(package);
            await db.SaveChangesAsync(cancellationToken);
            return package;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("下载 CLI 包超时（最长等待 30 分钟），请检查下载地址或网络连接。", ex);
        }
    }

    public async Task<CliPackage> InstallFromStreamAsync(
        ProviderType providerType,
        string version,
        string fileName,
        Stream stream,
        string? expectedSha256,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(version)) throw new InvalidOperationException("版本号不能为空。");
        version = version.Trim();
        await using (var db = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var operatingSystem = TunnelProcessSupervisor.CurrentOs();
            var architecture = TunnelProcessSupervisor.CurrentArchitecture();
            var existing = await db.CliPackages.AsNoTracking().SingleOrDefaultAsync(
                package => package.ProviderType == providerType
                           && package.Version == version
                           && package.OperatingSystem == operatingSystem
                           && package.Architecture == architecture,
                cancellationToken);
            if (existing is not null && existing.DeletedAt is null && !replaceExisting)
                throw new InvalidOperationException($"{providerType} {version} 已安装，请确认后再替换。");
        }

        fileName = Path.GetFileName(fileName);
        var packageId = Guid.NewGuid();
        var installDirectory = Path.Combine(_dataDirectory, "cli", providerType.ToString(), packageId.ToString("N"));
        var stagingDirectory = installDirectory + ".staging";
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            var archivePath = Path.Combine(stagingDirectory, fileName);

            string sha256;
            await using (var target = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, FileOptions.Asynchronous))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81_920];
                long total = 0;
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    total += read;
                    if (total > _options.MaxPackageBytes) throw new InvalidOperationException("CLI 包超过大小限制。");
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256) && !NormalizeHash(expectedSha256).Equals(sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("CLI 包 SHA-256 校验失败。");

            var contentDirectory = Path.Combine(stagingDirectory, "content");
            Directory.CreateDirectory(contentDirectory);
            string? directExecutable = null;
            try
            {
                switch (DetectPackageContent(archivePath))
                {
                    case CliPackageContentKind.Zip:
                        ExtractZip(archivePath, contentDirectory);
                        break;
                    case CliPackageContentKind.Tar:
                        ExtractTar(archivePath, contentDirectory);
                        break;
                    case CliPackageContentKind.GZip when fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
                                                         || fileName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase):
                        ExtractTarGz(archivePath, contentDirectory);
                        break;
                    case CliPackageContentKind.GZip:
                        directExecutable = ExtractGZip(archivePath, contentDirectory, fileName);
                        break;
                    case CliPackageContentKind.Executable:
                        var targetName = providerType == ProviderType.MeFrp ? "mefrpc" : "frpc";
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) targetName += ".exe";
                        directExecutable = Path.Combine(contentDirectory, targetName);
                        File.Move(archivePath, directExecutable);
                        break;
                    case CliPackageContentKind.Unknown when fileName.EndsWith(".tar", StringComparison.OrdinalIgnoreCase):
                        ExtractTar(archivePath, contentDirectory);
                        break;
                    default:
                        throw new InvalidOperationException("CLI 文件既不是当前平台的可执行文件，也不是受支持的 ZIP、TAR 或 GZ 压缩包。");
                }
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidOperationException("CLI 压缩包格式无效或文件已损坏，请重新下载完整安装包后再上传。", ex);
            }

            var executable = directExecutable ?? FindExecutable(contentDirectory, providerType);
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
            await VerifyExecutableAsync(executable, cancellationToken);

            Directory.Move(stagingDirectory, installDirectory);
            executable = Path.Combine(installDirectory, Path.GetRelativePath(stagingDirectory, executable));
            var package = new CliPackage
            {
                Id = packageId,
                ProviderType = providerType,
                Version = version,
                OperatingSystem = TunnelProcessSupervisor.CurrentOs(),
                Architecture = TunnelProcessSupervisor.CurrentArchitecture(),
                Sha256 = sha256,
                InstallDirectory = installDirectory,
                ExecutablePath = executable
            };
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var existing = await db.CliPackages.SingleOrDefaultAsync(x =>
                x.ProviderType == providerType
                && x.Version == package.Version
                && x.OperatingSystem == package.OperatingSystem
                && x.Architecture == package.Architecture,
                cancellationToken);
            string? supersededDirectory = null;
            if (existing is null)
            {
                db.CliPackages.Add(package);
            }
            else
            {
                if (existing.DeletedAt is null && !replaceExisting)
                    throw new InvalidOperationException($"{providerType} {package.Version} 已安装，请确认后再替换。");

                supersededDirectory = existing.InstallDirectory;
                existing.Sha256 = package.Sha256;
                existing.InstallDirectory = package.InstallDirectory;
                existing.ExecutablePath = package.ExecutablePath;
                existing.SourceUrl = null;
                existing.InstalledAt = DateTimeOffset.UtcNow;
                existing.DeletedAt = null;
                package = existing;
            }
            await db.SaveChangesAsync(cancellationToken);
            if (!string.Equals(supersededDirectory, installDirectory, StringComparison.OrdinalIgnoreCase))
                TryDeleteDirectory(supersededDirectory);
            return package;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            TryDeleteDirectory(stagingDirectory);
            TryDeleteDirectory(installDirectory);
            throw new InvalidOperationException(DuplicatePackageMessage(providerType, version), ex);
        }
        catch
        {
            TryDeleteDirectory(stagingDirectory);
            TryDeleteDirectory(installDirectory);
            throw;
        }
    }

    public async Task ActivateAsync(Guid packageId, bool restartRunningTunnels, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var package = await db.CliPackages.SingleOrDefaultAsync(x => x.Id == packageId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("CLI 包不存在。");
        var running = restartRunningTunnels
            ? await db.Tunnels.Include(x => x.ProviderAccount).Where(x => x.ProviderAccount.ProviderType == package.ProviderType && x.DesiredState == TunnelDesiredState.Running).Select(x => x.Id).ToListAsync(cancellationToken)
            : [];
        foreach (var tunnelId in running) await supervisor.StopAsync(tunnelId, "切换 CLI 版本。", cancellationToken);

        await db.CliPackages.Where(x => x.ProviderType == package.ProviderType && x.OperatingSystem == package.OperatingSystem && x.Architecture == package.Architecture && x.DeletedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.IsActive, false), cancellationToken);
        package.IsActive = true;
        db.CliPackages.Update(package);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var tunnelId in running) await supervisor.StartAsync(tunnelId, cancellationToken);
    }

    public async Task DeleteAsync(Guid packageId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var package = await db.CliPackages.SingleOrDefaultAsync(x => x.Id == packageId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("CLI 包不存在。");
        if (package.IsActive) throw new InvalidOperationException("活动 CLI 不能删除。");

        package.IsActive = false;
        package.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        TryDeleteDirectory(package.InstallDirectory);
    }

    internal static void ExtractZip(string archivePath, string destination)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var target = SafeDestination(destination, entry.FullName);
            if (string.IsNullOrEmpty(entry.Name)) Directory.CreateDirectory(target);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, false);
            }
        }
    }

    private static void ExtractTarGz(string archivePath, string destination)
    {
        using var file = File.OpenRead(archivePath);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        while (reader.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.Directory)) continue;
            var target = SafeDestination(destination, entry.Name);
            if (entry.EntryType == TarEntryType.Directory) Directory.CreateDirectory(target);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var output = File.Create(target);
                entry.DataStream?.CopyTo(output);
            }
        }
    }

    internal static void ExtractTar(string archivePath, string destination)
    {
        using var file = File.OpenRead(archivePath);
        ExtractTar(file, destination);
    }

    private static void ExtractTar(Stream stream, string destination)
    {
        using var reader = new TarReader(stream);
        while (reader.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.Directory)) continue;
            var target = SafeDestination(destination, entry.Name);
            if (entry.EntryType == TarEntryType.Directory) Directory.CreateDirectory(target);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var output = File.Create(target);
                entry.DataStream?.CopyTo(output);
            }
        }
    }

    private static string SafeDestination(string root, string name)
    {
        var rootPath = Path.GetFullPath(root);
        var destination = Path.GetFullPath(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.Equals(rootPath, StringComparison.OrdinalIgnoreCase)
            && !destination.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("压缩包包含非法路径。");
        return destination;
    }

    internal static string FindExecutable(string directory, ProviderType providerType)
    {
        var extension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";
        var preferredName = providerType == ProviderType.MeFrp ? $"mefrpc{extension}" : $"frpc{extension}";
        var suffix = $"frpc{extension}";
        var candidates = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(path => Path.GetFileName(path).Equals(preferredName, StringComparison.OrdinalIgnoreCase))
            .ThenBy(path => path.Count(character => character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return candidates.FirstOrDefault()
            ?? throw new InvalidOperationException($"CLI 包内未找到当前平台所需的 *{suffix}。");
    }

    internal static async Task VerifyExecutableAsync(string executable, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await using var stream = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read, 4, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var read = await stream.ReadAsync(header, cancellationToken);
        var valid = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? read >= 2 && header[0] == (byte)'M' && header[1] == (byte)'Z'
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                && read == 4 && header[0] == 0x7f && header[1] == (byte)'E' && header[2] == (byte)'L' && header[3] == (byte)'F';
        if (!valid)
        {
            var format = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows PE" : "Linux ELF";
            throw new InvalidOperationException($"CLI 可执行文件不是有效的 {format} 文件。");
        }
    }

    internal static string ExtractGZip(string archivePath, string destination, string fileName)
    {
        var outputName = Path.GetFileNameWithoutExtension(Path.GetFileName(fileName));
        if (string.IsNullOrWhiteSpace(outputName)) outputName = "frpc";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && !outputName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            outputName += ".exe";
        }

        Directory.CreateDirectory(destination);
        var outputPath = SafeDestination(destination, outputName);
        using var file = File.OpenRead(archivePath);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var output = File.Create(outputPath);
        gzip.CopyTo(output);
        return outputPath;
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (!Directory.Exists(path)) return;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string NormalizeHash(string value) => value.Trim().Replace("sha256:", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "");

    internal static CliPackageContentKind DetectPackageContent(string path)
    {
        Span<byte> header = stackalloc byte[512];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var read = stream.Read(header);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && read >= 2 && header[0] == (byte)'M' && header[1] == (byte)'Z')
            return CliPackageContentKind.Executable;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            && read >= 4 && header[0] == 0x7f && header[1] == (byte)'E' && header[2] == (byte)'L' && header[3] == (byte)'F')
            return CliPackageContentKind.Executable;
        if (read >= 4 && header[0] == (byte)'P' && header[1] == (byte)'K'
            && ((header[2] == 3 && header[3] == 4) || (header[2] == 5 && header[3] == 6) || (header[2] == 7 && header[3] == 8)))
            return CliPackageContentKind.Zip;
        if (read >= 2 && header[0] == 0x1f && header[1] == 0x8b)
            return CliPackageContentKind.GZip;
        if (read >= 262 && header[257] == (byte)'u' && header[258] == (byte)'s' && header[259] == (byte)'t'
                        && header[260] == (byte)'a' && header[261] == (byte)'r')
            return CliPackageContentKind.Tar;
        return CliPackageContentKind.Unknown;
    }

    private static string DuplicatePackageMessage(ProviderType providerType, string version) =>
        $"{providerType} 的 {version} 版本已安装；如需保留另一份构建，请填写不同的版本号（例如 {version}-repack）。";
}

internal enum CliPackageContentKind
{
    Unknown,
    Executable,
    Zip,
    Tar,
    GZip
}
