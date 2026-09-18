using Frpm.Domain.Enums;

namespace Frpm.Abstractions.Models;

public static class TunnelInputRules
{
    private static readonly IReadOnlyList<string> StandardTypes = ["tcp", "udp", "http", "https"];
    private static readonly IReadOnlyList<string> SakuraTypes = ["tcp", "udp", "http", "https", "wol", "etcp", "eudp"];

    public static IReadOnlyList<string> TypesFor(ProviderType providerType) =>
        providerType == ProviderType.SakuraFrp ? SakuraTypes : StandardTypes;

    public static bool IsDomainType(string? type) =>
        type is not null && (type.Equals("http", StringComparison.OrdinalIgnoreCase)
            || type.Equals("https", StringComparison.OrdinalIgnoreCase));

    public static bool IsUdpType(string? type) =>
        type is not null && (type.Equals("udp", StringComparison.OrdinalIgnoreCase)
            || type.Equals("eudp", StringComparison.OrdinalIgnoreCase));

    public static bool RequiresLocalEndpoint(ProviderType providerType, string? type) =>
        providerType != ProviderType.SakuraFrp || !string.Equals(type, "wol", StringComparison.OrdinalIgnoreCase);

    public static bool UsesRemotePort(ProviderType providerType, string? type) =>
        RequiresLocalEndpoint(providerType, type) && !IsDomainType(type);

    public static int MaximumDomains(ProviderType providerType) => providerType switch
    {
        ProviderType.LoliaFrp => 1,
        ProviderType.SakuraFrp => 3,
        _ => int.MaxValue
    };

    public static IReadOnlyList<string> ParseDomains(string? value) => string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split([',', ';', '\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static string? NormalizeDomains(string? value)
    {
        var domains = ParseDomains(value);
        return domains.Count == 0 ? null : string.Join(", ", domains);
    }
}
