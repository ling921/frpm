using Frpm.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Frpm.Infrastructure.Providers;

[SingletonService(typeof(IFrpProviderFactory))]
public sealed class FrpProviderFactory(IServiceProvider services) : IFrpProviderFactory
{
    public IFrpProvider Get(ProviderType type) => services.GetRequiredKeyedService<IFrpProvider>(type.ToString());
}
