using System.Text.Json;
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
    private static readonly Dictionary<ProviderType, BuiltInProviderDefinition> Defaults = new()
    {
        [ProviderType.SakuraFrp] = new(
            ProviderType.SakuraFrp,
            "SakuraFrp",
            "https://api.natfrp.com/v4",
            false,
            new ProviderCapabilitiesModel(
                CanCreate: true,
                CanEdit: true,
                CanDelete: true,
                HasNodes: true,
                SupportsDomains: true,
                SupportsRemotePort: true,
                SupportedProtocols: ["tcp", "udp", "http", "https", "wol", "etcp", "eudp"],
                SupportsDualProtocol: false,
                RequiresLocalEndpoint: true,
                MaximumDomains: 3,
                RequiresCustomHttpDomain: false,
                SupportsKick: false,
                SupportsRemoteDisable: true,
                SupportsTokenReset: false,
                SupportsTrafficStats: true,
                SupportsNodeStatus: true,
                SupportsAnnouncements: true)),

        [ProviderType.LoliaFrp] = new(
            ProviderType.LoliaFrp,
            "LoliaFrp",
            "https://api.lolia.link/api/v1",
            true,
            new ProviderCapabilitiesModel(
                CanCreate: true,
                CanEdit: true,
                CanDelete: true,
                HasNodes: true,
                SupportsDomains: true,
                SupportsRemotePort: true,
                SupportedProtocols: ["tcp", "udp", "tcp+udp", "http", "https"],
                SupportsDualProtocol: true,
                RequiresLocalEndpoint: true,
                MaximumDomains: 1,
                RequiresCustomHttpDomain: false,
                SupportsKick: false,
                SupportsRemoteDisable: false,
                SupportsTokenReset: true,
                SupportsTrafficStats: true,
                SupportsNodeStatus: true,
                SupportsAnnouncements: true)),

        [ProviderType.MeFrp] = new(
            ProviderType.MeFrp,
            "ME Frp",
            "https://api.mefrp.com/api",
            false,
            new ProviderCapabilitiesModel(
                CanCreate: true,
                CanEdit: true,
                CanDelete: true,
                HasNodes: true,
                SupportsDomains: true,
                SupportsRemotePort: true,
                SupportedProtocols: ["tcp", "udp", "http", "https"],
                SupportsDualProtocol: false,
                RequiresLocalEndpoint: true,
                MaximumDomains: 1,
                RequiresCustomHttpDomain: true,
                SupportsKick: true,
                SupportsRemoteDisable: true,
                SupportsTokenReset: true,
                SupportsTrafficStats: true,
                SupportsNodeStatus: true,
                SupportsAnnouncements: true))
    };

    private static readonly Dictionary<ProviderType, ProviderCapabilitiesModel> Overrides = [];
    private static readonly object LockObj = new();

    public static IReadOnlyList<BuiltInProviderDefinition> All
    {
        get
        {
            lock (LockObj)
            {
                return Defaults.Values.Select(def =>
                    Overrides.TryGetValue(def.Type, out var over)
                        ? def with { Capabilities = over }
                        : def).ToList();
            }
        }
    }

    public static BuiltInProviderDefinition Get(ProviderType type)
    {
        lock (LockObj)
        {
            if (!Defaults.TryGetValue(type, out var def))
                throw new NotSupportedException($"不支持供应商 {type}。");

            return Overrides.TryGetValue(type, out var over)
                ? def with { Capabilities = over }
                : def;
        }
    }

    public static void ApplyOverrides(string jsonContent)
    {
        if (string.IsNullOrWhiteSpace(jsonContent)) return;
        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;
            var providersElem = root.TryGetProperty("Providers", out var pe) ? pe : root;
            if (providersElem.ValueKind != JsonValueKind.Object) return;

            lock (LockObj)
            {
                foreach (var prop in providersElem.EnumerateObject())
                {
                    if (Enum.TryParse<ProviderType>(prop.Name, true, out var providerType)
                        && Defaults.TryGetValue(providerType, out var def))
                    {
                        var merged = MergeCapabilities(def.Capabilities, prop.Value);
                        Overrides[providerType] = merged;
                    }
                }
            }
        }
        catch
        {
            // Ignore malformed override JSON
        }
    }

    public static void TryLoadOverrides(string filePath)
    {
        if (File.Exists(filePath))
        {
            try
            {
                var content = File.ReadAllText(filePath);
                ApplyOverrides(content);
            }
            catch
            {
                // Silently ignore file read errors
            }
        }
    }

    public static void ResetOverrides()
    {
        lock (LockObj)
        {
            Overrides.Clear();
        }
    }

    private static ProviderCapabilitiesModel MergeCapabilities(ProviderCapabilitiesModel orig, JsonElement elem)
    {
        var canCreate = elem.TryGetProperty("CanCreate", out var cc) && cc.ValueKind is JsonValueKind.True or JsonValueKind.False ? cc.GetBoolean() : orig.CanCreate;
        var canEdit = elem.TryGetProperty("CanEdit", out var ce) && ce.ValueKind is JsonValueKind.True or JsonValueKind.False ? ce.GetBoolean() : orig.CanEdit;
        var canDelete = elem.TryGetProperty("CanDelete", out var cd) && cd.ValueKind is JsonValueKind.True or JsonValueKind.False ? cd.GetBoolean() : orig.CanDelete;
        var hasNodes = elem.TryGetProperty("HasNodes", out var hn) && hn.ValueKind is JsonValueKind.True or JsonValueKind.False ? hn.GetBoolean() : orig.HasNodes;
        var supportsDomains = elem.TryGetProperty("SupportsDomains", out var sd) && sd.ValueKind is JsonValueKind.True or JsonValueKind.False ? sd.GetBoolean() : orig.SupportsDomains;
        var supportsRemotePort = elem.TryGetProperty("SupportsRemotePort", out var srp) && srp.ValueKind is JsonValueKind.True or JsonValueKind.False ? srp.GetBoolean() : orig.SupportsRemotePort;

        IReadOnlyList<string>? supportedProtocols = orig.SupportedProtocols;
        if (elem.TryGetProperty("SupportedProtocols", out var sp) && sp.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in sp.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    list.Add(item.GetString()!);
            }
            if (list.Count > 0) supportedProtocols = list;
        }

        var supportsDualProtocol = elem.TryGetProperty("SupportsDualProtocol", out var sdp) && sdp.ValueKind is JsonValueKind.True or JsonValueKind.False ? sdp.GetBoolean() : orig.SupportsDualProtocol;
        var requiresLocalEndpoint = elem.TryGetProperty("RequiresLocalEndpoint", out var rle) && rle.ValueKind is JsonValueKind.True or JsonValueKind.False ? rle.GetBoolean() : orig.RequiresLocalEndpoint;
        var maximumDomains = elem.TryGetProperty("MaximumDomains", out var md) && md.ValueKind == JsonValueKind.Number ? md.GetInt32() : orig.MaximumDomains;
        var requiresCustomHttpDomain = elem.TryGetProperty("RequiresCustomHttpDomain", out var rchd) && rchd.ValueKind is JsonValueKind.True or JsonValueKind.False ? rchd.GetBoolean() : orig.RequiresCustomHttpDomain;
        var supportsKick = elem.TryGetProperty("SupportsKick", out var sk) && sk.ValueKind is JsonValueKind.True or JsonValueKind.False ? sk.GetBoolean() : orig.SupportsKick;
        var supportsRemoteDisable = elem.TryGetProperty("SupportsRemoteDisable", out var srd) && srd.ValueKind is JsonValueKind.True or JsonValueKind.False ? srd.GetBoolean() : orig.SupportsRemoteDisable;
        var supportsTokenReset = elem.TryGetProperty("SupportsTokenReset", out var str) && str.ValueKind is JsonValueKind.True or JsonValueKind.False ? str.GetBoolean() : orig.SupportsTokenReset;
        var supportsTrafficStats = elem.TryGetProperty("SupportsTrafficStats", out var sts) && sts.ValueKind is JsonValueKind.True or JsonValueKind.False ? sts.GetBoolean() : orig.SupportsTrafficStats;
        var supportsNodeStatus = elem.TryGetProperty("SupportsNodeStatus", out var sns) && sns.ValueKind is JsonValueKind.True or JsonValueKind.False ? sns.GetBoolean() : orig.SupportsNodeStatus;
        var supportsAnnouncements = elem.TryGetProperty("SupportsAnnouncements", out var sa) && sa.ValueKind is JsonValueKind.True or JsonValueKind.False ? sa.GetBoolean() : orig.SupportsAnnouncements;

        return new ProviderCapabilitiesModel(
            canCreate, canEdit, canDelete, hasNodes, supportsDomains, supportsRemotePort,
            supportedProtocols, supportsDualProtocol, requiresLocalEndpoint, maximumDomains,
            requiresCustomHttpDomain, supportsKick, supportsRemoteDisable, supportsTokenReset,
            supportsTrafficStats, supportsNodeStatus, supportsAnnouncements);
    }
}
