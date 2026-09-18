using Frpm.Abstractions.Models;
using Frpm.Abstractions.Services;
using Frpm.Data;
using Frpm.Infrastructure.Runtime;
using Ling.RemoteServices.Models;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace Frpm.Infrastructure.Services;

[ScopedService(typeof(ICliPackageService))]
public sealed class CliPackageService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ICliPackageManager packageManager,
    IMapper mapper) : ICliPackageService
{
    public async Task<IReadOnlyList<CliPackageModel>> GetListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.CliPackages.AsNoTracking().Where(x => x.DeletedAt == null).OrderBy(x => x.ProviderType).ThenByDescending(x => x.InstalledAt)
            .ProjectToType<CliPackageModel>()
            .ToListAsync(cancellationToken);
    }

    public async Task<CliPackageModel> InstallFromUrlAsync(InstallCliFromUrlRequest request, CancellationToken cancellationToken = default)
    {
        var package = await packageManager.InstallFromUrlAsync(request.ProviderType, request.Version, request.Url, request.Sha256, request.ReplaceExisting, cancellationToken);
        if (await ShouldActivateAsync(dbContextFactory, package, request.ActivateAfterInstall, cancellationToken))
        {
            await packageManager.ActivateAsync(package.Id, true, cancellationToken);
            package.IsActive = true;
        }
        return mapper.Map<CliPackageModel>(package);
    }

    public async Task<CliPackageModel> UploadAsync(
        Frpm.Domain.Enums.ProviderType providerType,
        string version,
        RemoteUploadFile file,
        string? sha256,
        bool replaceExisting,
        bool activateAfterInstall,
        CancellationToken cancellationToken = default)
    {
        var package = await packageManager.InstallFromStreamAsync(providerType, version, file.FileName, file.Content, sha256, replaceExisting, cancellationToken);
        if (await ShouldActivateAsync(dbContextFactory, package, activateAfterInstall, cancellationToken))
        {
            await packageManager.ActivateAsync(package.Id, true, cancellationToken);
            package.IsActive = true;
        }
        return mapper.Map<CliPackageModel>(package);
    }

    public Task ActivateAsync(
        Guid id,
        bool restartRunningTunnels,
        CancellationToken cancellationToken = default) =>
        packageManager.ActivateAsync(id, restartRunningTunnels, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        packageManager.DeleteAsync(id, cancellationToken);

    internal static async Task<bool> ShouldActivateAsync(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        Frpm.Domain.Entities.CliPackage package,
        bool activateAfterInstall,
        CancellationToken cancellationToken)
    {
        if (package.IsActive || activateAfterInstall) return true;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var hasActivePackage = await db.CliPackages.AsNoTracking().AnyAsync(candidate =>
            candidate.Id != package.Id
            && candidate.ProviderType == package.ProviderType
            && candidate.OperatingSystem == package.OperatingSystem
            && candidate.Architecture == package.Architecture
            && candidate.IsActive
            && candidate.DeletedAt == null,
            cancellationToken);
        return !hasActivePackage;
    }
}
