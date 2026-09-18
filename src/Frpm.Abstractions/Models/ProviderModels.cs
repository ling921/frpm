using Frpm.Domain.Enums;

namespace Frpm.Abstractions.Models;

public sealed record ProviderCapabilitiesModel(
    bool CanCreate,
    bool CanEdit,
    bool CanDelete,
    bool HasNodes,
    bool SupportsDomains,
    bool SupportsRemotePort);

public sealed record ProviderAccountModel(
    Guid Id, ProviderType ProviderType, string DisplayName, string ApiBaseUrl, bool Enabled,
    DateTimeOffset? LastSyncAt, bool? LastSyncSucceeded, string? LastSyncMessage, int TunnelCount,
    ProviderCapabilitiesModel Capabilities);

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
    string? Description = null);
