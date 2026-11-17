using Frpm.Domain.Enums;

namespace Frpm.Abstractions.Models;

public static class TunnelInputRules
{
    public static IReadOnlyList<string> TypesFor(ProviderCapabilitiesModel capabilities, bool isEditing = false)
    {
        var protocols = capabilities.Protocols;
        return isEditing
            ? protocols.Where(t => !string.Equals(t, "tcp+udp", StringComparison.OrdinalIgnoreCase)).ToList()
            : protocols;
    }

    public static IReadOnlyList<string> TypesFor(ProviderType providerType, bool isEditing = false) =>
        TypesFor(BuiltInProviderCatalog.Get(providerType).Capabilities, isEditing);

    public static bool IsDomainType(string? type) =>
        type is not null && (type.Equals("http", StringComparison.OrdinalIgnoreCase)
            || type.Equals("https", StringComparison.OrdinalIgnoreCase));

    public static bool IsUdpType(string? type) =>
        type is not null && (type.Equals("udp", StringComparison.OrdinalIgnoreCase)
            || type.Equals("eudp", StringComparison.OrdinalIgnoreCase));

    public static bool RequiresLocalEndpoint(ProviderCapabilitiesModel capabilities, string? type) =>
        !string.Equals(type, "wol", StringComparison.OrdinalIgnoreCase) && capabilities.RequiresLocalEndpoint;

    public static bool RequiresLocalEndpoint(ProviderType providerType, string? type) =>
        RequiresLocalEndpoint(BuiltInProviderCatalog.Get(providerType).Capabilities, type);

    public static bool UsesRemotePort(ProviderCapabilitiesModel capabilities, string? type) =>
        RequiresLocalEndpoint(capabilities, type) && !IsDomainType(type) && capabilities.SupportsRemotePort;

    public static bool UsesRemotePort(ProviderType providerType, string? type) =>
        UsesRemotePort(BuiltInProviderCatalog.Get(providerType).Capabilities, type);

    public static int MaximumDomains(ProviderCapabilitiesModel capabilities) => capabilities.MaximumDomains;

    public static int MaximumDomains(ProviderType providerType) =>
        MaximumDomains(BuiltInProviderCatalog.Get(providerType).Capabilities);

    public static bool RequiresCustomHttpDomain(ProviderCapabilitiesModel capabilities) =>
        capabilities.RequiresCustomHttpDomain;

    public static bool RequiresCustomHttpDomain(ProviderType providerType) =>
        RequiresCustomHttpDomain(BuiltInProviderCatalog.Get(providerType).Capabilities);

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
