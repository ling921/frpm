using Frpm.Domain.Enums;

namespace Frpm.Domain.Entities;

public sealed class Tunnel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProviderAccountId { get; set; }
    public ProviderAccount ProviderAccount { get; set; } = null!;
    public required string RemoteId { get; set; }
    public required string Name { get; set; }
    public string? Remark { get; set; }
    public required string Type { get; set; }
    public string? NodeId { get; set; }
    public string? NodeName { get; set; }
    public string? LocalAddress { get; set; }
    public int? LocalPort { get; set; }
    public string? RemoteAddress { get; set; }
    public string ProviderPayloadJson { get; set; } = "{}";
    public string? ProviderStatus { get; set; }
    public TunnelRemoteState RemoteState { get; set; } = TunnelRemoteState.Unknown;
    public TunnelDesiredState DesiredState { get; set; } = TunnelDesiredState.Stopped;
    public TunnelRuntimeState RuntimeState { get; set; } = TunnelRuntimeState.Stopped;
    public string? RuntimeMessage { get; set; }
    public DateTimeOffset LastSeenRemoteAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
    public List<TunnelRun> Runs { get; set; } = [];
}
