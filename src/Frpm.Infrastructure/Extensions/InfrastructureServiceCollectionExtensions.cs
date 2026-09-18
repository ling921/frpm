using Frpm.Data;
using Frpm.Infrastructure.Options;
using Frpm.Infrastructure.Runtime;
using Frpm.Infrastructure.Sync;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Frpm.Infrastructure.Extensions;

[AutoInjectExtensions(MethodName = "AddFrpmInfrastructure", IncludeConfiguration = true)]
public static partial class InfrastructureServiceCollectionExtensions
{
    static partial void AddAdditionalServices(IServiceCollection services, IConfiguration configuration)
    {
        var storage = configuration.GetSection(FrpmStorageOptions.SectionName).Get<FrpmStorageOptions>() ?? new FrpmStorageOptions();
        storage.DataDirectory = Path.GetFullPath(storage.DataDirectory);
        Directory.CreateDirectory(storage.DataDirectory);
        Directory.CreateDirectory(Path.Combine(storage.DataDirectory, "keys"));
        services.Configure<FrpmStorageOptions>(options =>
        {
            options.DataDirectory = storage.DataDirectory;
            options.LogRetentionDays = storage.LogRetentionDays;
            options.MaxLogBytes = storage.MaxLogBytes;
            options.MaxPackageBytes = storage.MaxPackageBytes;
        });

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? $"Data Source={Path.Combine(storage.DataDirectory, "frpm.db")}";
        services.AddDbContextFactory<ApplicationDbContext>(options => options.UseSqlite(connectionString));
        services.AddHttpContextAccessor();
        services.AddDataProtection().SetApplicationName("Frpm").PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(storage.DataDirectory, "keys")));
        services.AddHttpClient("frp-provider", client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient("cli-download", client => client.Timeout = TimeSpan.FromMinutes(30));

        FrpmMapsterConfiguration.Configure(TypeAdapterConfig.GlobalSettings);
        services.AddSingleton(TypeAdapterConfig.GlobalSettings);
        services.AddScoped<IMapper, ServiceMapper>();
        services.AddHostedService<ProcessSupervisorHostedService>();
        services.AddHostedService<SystemRuntimeHostedService>();
        services.AddHostedService<TunnelSyncWorker>();
        services.AddHostedService<StorageCleanupWorker>();
    }
}
