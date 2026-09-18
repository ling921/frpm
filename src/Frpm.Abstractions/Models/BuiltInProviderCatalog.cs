using Frpm.Domain.Enums;

namespace Frpm.Abstractions.Models;

public sealed record BuiltInProviderDefinition(
    ProviderType Type,
    string DisplayName,
    string DefaultApiBaseUrl,
    bool SupportsOAuth,
    ProviderCapabilitiesModel Capabilities);

public static class BuiltInProviderCatalog
{
    public static IReadOnlyList<BuiltInProviderDefinition> All { get; } =
    [
        new(
            ProviderType.SakuraFrp,
            "SakuraFrp",
            "https://api.natfrp.com/v4",
            false,
            new(true, true, true, true, true, true)),
        new(
            ProviderType.LoliaFrp,
            "LoliaFrp",
            "https://api.lolia.link/api/v1",
            true,
            new(true, true, true, true, true, true)),
        new(
            ProviderType.MeFrp,
            "ME Frp",
            "https://api.mefrp.com/api",
            false,
            new(true, true, true, true, true, true))
    ];

    public static BuiltInProviderDefinition Get(ProviderType type) =>
        All.FirstOrDefault(provider => provider.Type == type)
        ?? throw new NotSupportedException($"不支持供应商 {type}。");
}
