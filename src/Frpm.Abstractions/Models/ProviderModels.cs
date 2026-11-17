using Frpm.Domain.Enums;

namespace Frpm.Abstractions.Models;

public sealed record ProviderCapabilitiesModel(
    bool CanCreate = true,
    bool CanEdit = true,
    bool CanDelete = true,
    bool HasNodes = true,
    bool SupportsDomains = true,
    bool SupportsRemotePort = true,
    IReadOnlyList<string>? SupportedProtocols = null,
    bool SupportsDualProtocol = false,
    bool RequiresLocalEndpoint = true,
    int MaximumDomains = 1,
    bool RequiresCustomHttpDomain = false,
    bool SupportsKick = false,
    bool SupportsRemoteDisable = false,
    bool SupportsTokenReset = false,
    bool SupportsTrafficStats = true,
    bool SupportsNodeStatus = true,
    bool SupportsAnnouncements = false)
{
    public IReadOnlyList<string> Protocols => SupportedProtocols ?? ["tcp", "udp", "http", "https"];
}

public sealed record ProviderAccountModel(
    Guid Id, ProviderType ProviderType, string DisplayName, string ApiBaseUrl, bool Enabled,
    DateTimeOffset? LastSyncAt, bool? LastSyncSucceeded, string? LastSyncMessage, int TunnelCount,
    ProviderCapabilitiesModel Capabilities,
    string? ClientId = null,
    bool HasClientSecret = false,
    long? TrafficLimitBytes = null,
    long? TrafficUsedBytes = null,
    long? BandwidthLimitBytes = null,
    string? UserRole = null);

public sealed class SaveProviderAccountRequest
{
    public Guid? Id { get; set; }
    public ProviderType ProviderType { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}

public sealed record ProviderNodeModel(
    string Id,
    string Name,
    string? Address,
    bool Available,
    string? Region = null,
    string? Bandwidth = null,
    string? AllowedTypes = null,
    string? PortRange = null,
    string? Description = null,
    int? LoadPercent = null,
    bool IsOnline = true,
    bool NeedKyc = false,
    bool NeedBeian = false,
    bool IsVip = false,
    double? TrafficRatio = null,
    string? RegionGroup = null);
