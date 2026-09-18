namespace Frpm.Domain.Entities;

public sealed class TunnelRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TunnelId { get; set; }
    public Tunnel Tunnel { get; set; } = null!;
    public Guid CliPackageId { get; set; }
    public CliPackage CliPackage { get; set; } = null!;
    public int? ProcessId { get; set; }
    public DateTimeOffset? ProcessStartedAt { get; set; }
    public required string ExecutablePath { get; set; }
    public required string LogPath { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }
    public int? ExitCode { get; set; }
    public bool StopRequested { get; set; }
    public string? Summary { get; set; }
}
