using Frpm.Abstractions.Models;
using Frpm.Domain.Enums;

namespace Frpm.Infrastructure.Tests;

public sealed class CliDownloadCatalogTests
{
    [Theory]
    [InlineData(ProviderType.SakuraFrp, "windows", "amd64", "Windows 64 位 (x86_64, amd64)")]
    [InlineData(ProviderType.SakuraFrp, "windows", "386", "Windows 32 位 (i386)")]
    [InlineData(ProviderType.SakuraFrp, "windows", "arm64", "Windows AArch64 (arm64)")]
    [InlineData(ProviderType.SakuraFrp, "linux", "amd64", "Linux 64 位 (x86_64, amd64)")]
    [InlineData(ProviderType.SakuraFrp, "linux", "386", "Linux 32 位 (i386)")]
    [InlineData(ProviderType.SakuraFrp, "linux", "arm64", "Linux AArch64 (arm64)")]
    [InlineData(ProviderType.MeFrp, "windows", "amd64", "Windows · amd64")]
    [InlineData(ProviderType.MeFrp, "windows", "386", "Windows · 386")]
    [InlineData(ProviderType.MeFrp, "windows", "arm", "Windows · arm")]
    [InlineData(ProviderType.MeFrp, "windows", "arm64", "Windows · arm64")]
    [InlineData(ProviderType.MeFrp, "linux", "amd64", "Linux · amd64")]
    [InlineData(ProviderType.MeFrp, "linux", "386", "Linux · 386")]
    [InlineData(ProviderType.MeFrp, "linux", "arm", "Linux · arm")]
    [InlineData(ProviderType.MeFrp, "linux", "arm64", "Linux · arm64")]
    [InlineData(ProviderType.LoliaFrp, "windows", "amd64", "Windows · amd64")]
    [InlineData(ProviderType.LoliaFrp, "windows", "386", "Windows · 386")]
    [InlineData(ProviderType.LoliaFrp, "windows", "arm64", "Windows · arm64")]
    [InlineData(ProviderType.LoliaFrp, "linux", "amd64", "Linux · amd64")]
    [InlineData(ProviderType.LoliaFrp, "linux", "386", "Linux · 386")]
    [InlineData(ProviderType.LoliaFrp, "linux", "arm64", "Linux · arm64")]
    public void Recommend_returns_provider_specific_platform_choice(
        ProviderType providerType,
        string operatingSystem,
        string architecture,
        string expectedDisplayText)
    {
        var recommendation = BuiltInCliDownloadCatalog.Recommend(providerType, operatingSystem, architecture);

        Assert.True(recommendation.IsSupported);
        Assert.Equal(expectedDisplayText, recommendation.RecommendedDisplayText);
    }

    [Theory]
    [InlineData("windows", "386", "LoliaFrp_windows_386.zip")]
    [InlineData("windows", "amd64", "LoliaFrp_windows_amd64.zip")]
    [InlineData("windows", "arm64", "LoliaFrp_windows_arm64.zip")]
    [InlineData("linux", "386", "LoliaFrp_linux_386.tar.gz")]
    [InlineData("linux", "amd64", "LoliaFrp_linux_amd64.tar.gz")]
    [InlineData("linux", "arm64", "LoliaFrp_linux_arm64.tar.gz")]
    public void Recommend_uses_exact_lolia_asset_file_name(
        string operatingSystem,
        string architecture,
        string expectedFileName)
    {
        var recommendation = BuiltInCliDownloadCatalog.Recommend(
            ProviderType.LoliaFrp,
            operatingSystem,
            architecture);

        Assert.True(recommendation.IsSupported);
        Assert.Equal(expectedFileName, recommendation.ExpectedFileName);
    }

    [Fact]
    public void Recommend_maps_mefrp_loongarch64_to_loong64()
    {
        var recommendation = BuiltInCliDownloadCatalog.Recommend(ProviderType.MeFrp, "linux", "loongarch64");

        Assert.True(recommendation.IsSupported);
        Assert.Equal("loong64", recommendation.Architecture);
        Assert.Equal("Linux · loong64", recommendation.RecommendedDisplayText);
    }

    [Theory]
    [InlineData(ProviderType.SakuraFrp, "linux", "arm")]
    [InlineData(ProviderType.SakuraFrp, "freebsd", "amd64")]
    [InlineData(ProviderType.MeFrp, "linux", "riscv64")]
    [InlineData(ProviderType.LoliaFrp, "unknown", "unknown")]
    public void Recommend_does_not_guess_unsupported_target(
        ProviderType providerType,
        string operatingSystem,
        string architecture)
    {
        var recommendation = BuiltInCliDownloadCatalog.Recommend(providerType, operatingSystem, architecture);

        Assert.False(recommendation.IsSupported);
        Assert.Null(recommendation.RecommendedDisplayText);
        Assert.Null(recommendation.ExpectedFileName);
        Assert.NotNull(recommendation.Message);
    }
}
