using Frpm.Domain.Enums;

namespace Frpm.Abstractions.Models;

/// <summary>
/// Describes the official download page and supported platform choices for a provider CLI.
/// The catalog intentionally contains guidance only; it never treats a download-center page as a file URL.
/// </summary>
public sealed record ProviderCliDownloadGuide(
    ProviderType ProviderType,
    string OfficialDownloadUrl,
    IReadOnlyList<string> Steps,
    IReadOnlyList<CliDownloadPlatformOption> SupportedPlatforms);

/// <summary>
/// A provider-specific option presented by its official download page.
/// </summary>
public sealed record CliDownloadPlatformOption(
    string OperatingSystem,
    string Architecture,
    string DisplayText,
    string? ExpectedFileName = null);

/// <summary>
/// The download choice calculated for the operating system that runs FRPM and frpc.
/// </summary>
public sealed record CliDownloadRecommendation(
    bool IsSupported,
    string OperatingSystem,
    string Architecture,
    string? RecommendedDisplayText,
    string? ExpectedFileName,
    string? Message);

public static class BuiltInCliDownloadCatalog
{
    public static IReadOnlyList<ProviderCliDownloadGuide> All { get; } =
    [
        new(
            ProviderType.SakuraFrp,
            "https://www.natfrp.com/tunnel/download",
            [
                "在官方下载页先选择 frpc。",
                "在随后出现的列表中选择下方推荐的系统和架构。",
                "下载完成后，在本页下方上传安装包。"
            ],
            [
                new("windows", "amd64", "Windows 64 位 (x86_64, amd64)"),
                new("windows", "386", "Windows 32 位 (i386)"),
                new("windows", "arm64", "Windows AArch64 (arm64)"),
                new("linux", "amd64", "Linux 64 位 (x86_64, amd64)"),
                new("linux", "386", "Linux 32 位 (i386)"),
                new("linux", "arm64", "Linux AArch64 (arm64)")
            ]),
        new(
            ProviderType.MeFrp,
            "https://www.mefrp.com/dashboard/downloads",
            [
                "在下载中心先选择 Windows 或 Linux。",
                "再选择下方推荐的处理器架构。",
                "下载完成后，在本页下方上传安装包。"
            ],
            [
                new("windows", "386", "Windows · 386"),
                new("windows", "amd64", "Windows · amd64"),
                new("windows", "arm", "Windows · arm"),
                new("windows", "arm64", "Windows · arm64"),
                new("linux", "386", "Linux · 386"),
                new("linux", "amd64", "Linux · amd64"),
                new("linux", "arm", "Linux · arm"),
                new("linux", "arm64", "Linux · arm64"),
                new("linux", "loong64", "Linux · loong64")
            ]),
        new(
            ProviderType.LoliaFrp,
            "https://github.com/Lolia-FRP/lolia-frp/releases",
            [
                "在最新 Release 的 Assets 列表中查找下方推荐文件。",
                "可复制该资产旁的 SHA-256，并在上传时一并填写以校验文件。",
                "下载完成后，在本页下方上传安装包。"
            ],
            [
                new("windows", "386", "Windows · 386", "LoliaFrp_windows_386.zip"),
                new("windows", "amd64", "Windows · amd64", "LoliaFrp_windows_amd64.zip"),
                new("windows", "arm", "Windows · arm", "LoliaFrp_windows_arm.zip"),
                new("windows", "arm64", "Windows · arm64", "LoliaFrp_windows_arm64.zip"),
                new("linux", "386", "Linux · 386", "LoliaFrp_linux_386.tar.gz"),
                new("linux", "amd64", "Linux · amd64", "LoliaFrp_linux_amd64.tar.gz"),
                new("linux", "arm", "Linux · arm", "LoliaFrp_linux_arm.tar.gz"),
                new("linux", "arm64", "Linux · arm64", "LoliaFrp_linux_arm64.tar.gz")
            ])
    ];

    public static ProviderCliDownloadGuide Get(ProviderType providerType) =>
        All.FirstOrDefault(guide => guide.ProviderType == providerType)
        ?? throw new NotSupportedException($"不支持供应商 {providerType}。");

    public static CliDownloadRecommendation Recommend(
        ProviderType providerType,
        string? operatingSystem,
        string? architecture)
    {
        var normalizedSystem = NormalizeOperatingSystem(operatingSystem);
        var normalizedArchitecture = NormalizeArchitecture(architecture);
        var option = Get(providerType).SupportedPlatforms.FirstOrDefault(candidate =>
            candidate.OperatingSystem == normalizedSystem && candidate.Architecture == normalizedArchitecture);

        return option is not null
            ? new(true, normalizedSystem, normalizedArchitecture, option.DisplayText, option.ExpectedFileName, null)
            : new(
                false,
                normalizedSystem,
                normalizedArchitecture,
                null,
                null,
                $"暂未提供 {DisplayTarget(normalizedSystem, normalizedArchitecture)} 的自动下载建议，请在官方下载页确认可用版本。");
    }

    private static string NormalizeOperatingSystem(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "win" or "win32" or "windows" => "windows",
        "linux" => "linux",
        { Length: > 0 } system => system,
        _ => "unknown"
    };

    private static string NormalizeArchitecture(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "x64" or "x86_64" => "amd64",
        "x86" or "i386" or "i686" => "386",
        "aarch64" => "arm64",
        "loongarch64" => "loong64",
        { Length: > 0 } architecture => architecture,
        _ => "unknown"
    };

    private static string DisplayTarget(string operatingSystem, string architecture) =>
        $"{operatingSystem}/{architecture}";
}
