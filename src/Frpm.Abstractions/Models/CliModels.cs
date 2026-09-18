using Frpm.Domain.Enums;

namespace Frpm.Abstractions.Models;

public sealed record CliPackageModel(
    Guid Id, ProviderType ProviderType, string Version, string OperatingSystem, string Architecture,
    string Sha256, bool IsActive, DateTimeOffset InstalledAt, string? SourceUrl);

public sealed class InstallCliFromUrlRequest
{
    public ProviderType ProviderType { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Sha256 { get; set; }
    public bool ReplaceExisting { get; set; }
    public bool ActivateAfterInstall { get; set; }
}
