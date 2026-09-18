using Frpm.Domain.Entities;
using Frpm.Domain.Enums;

namespace Frpm.Infrastructure.Runtime;

public interface ICliPackageManager
{
    Task<CliPackage> InstallFromUrlAsync(ProviderType providerType, string version, string url, string? expectedSha256, bool replaceExisting, CancellationToken cancellationToken);
    Task<CliPackage> InstallFromStreamAsync(ProviderType providerType, string version, string fileName, Stream stream, string? expectedSha256, bool replaceExisting, CancellationToken cancellationToken);
    Task ActivateAsync(Guid packageId, bool restartRunningTunnels, CancellationToken cancellationToken);
    Task DeleteAsync(Guid packageId, CancellationToken cancellationToken);
}
