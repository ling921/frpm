using Frpm.Domain.Enums;

namespace Frpm.Domain.Entities;

public sealed class CliPackage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ProviderType ProviderType { get; set; }
    public required string Version { get; set; }
    public required string OperatingSystem { get; set; }
    public required string Architecture { get; set; }
    public required string Sha256 { get; set; }
    public required string InstallDirectory { get; set; }
    public required string ExecutablePath { get; set; }
    public string? SourceUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
    public List<TunnelRun> Runs { get; set; } = [];
}
