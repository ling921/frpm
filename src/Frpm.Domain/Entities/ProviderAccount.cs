using Frpm.Domain.Enums;

namespace Frpm.Domain.Entities;

public sealed class ProviderAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ProviderType ProviderType { get; set; }
    public required string DisplayName { get; set; }
    public required string ApiBaseUrl { get; set; }
    public required string ProtectedCredentials { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSyncAt { get; set; }
    public bool? LastSyncSucceeded { get; set; }
    public string? LastSyncMessage { get; set; }
    public List<Tunnel> Tunnels { get; set; } = [];
}
