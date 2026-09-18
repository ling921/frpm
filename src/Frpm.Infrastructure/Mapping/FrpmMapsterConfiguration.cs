using Frpm.Abstractions.Models;
using Frpm.Domain.Entities;
using Mapster;

namespace Frpm.Infrastructure.Extensions;

internal static class FrpmMapsterConfiguration
{
    public static void Configure(TypeAdapterConfig config)
    {
        config.NewConfig<Tunnel, TunnelModel>()
            .MapToConstructor(true)
            .Map(destination => destination.ProviderAccountName, source => source.ProviderAccount.DisplayName)
            .Map(destination => destination.ProviderType, source => source.ProviderAccount.ProviderType)
            .Map(destination => destination.ProviderDomain, _ => (string?)null)
            .Map(destination => destination.NodeAddress, _ => (string?)null);

        config.NewConfig<TunnelRun, TunnelRunModel>()
            .MapToConstructor(true)
            .Map(destination => destination.TunnelName, source => source.Tunnel.Name)
            .Map(destination => destination.CliVersion, source => source.CliPackage.Version);

        config.NewConfig<CliPackage, CliPackageModel>()
            .MapToConstructor(true);
    }
}
