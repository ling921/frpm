using Frpm.Abstractions.Models;
using Frpm.Abstractions.Services;
using Frpm.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Ling.RemoteServices.Models;

namespace Frpm.Application;

/// <summary>UI-independent use cases. Every operation owns its dependency scope.</summary>
public sealed class FrpmWorkspace(IServiceScopeFactory scopes)
{
    private async Task<T> UseAsync<TService, T>(Func<TService, Task<T>> operation) where TService : notnull
    {
        await using var scope = scopes.CreateAsyncScope();
        return await operation(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task UseAsync<TService>(Func<TService, Task> operation) where TService : notnull
    {
        await using var scope = scopes.CreateAsyncScope();
        await operation(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public Task<DashboardModel> GetDashboardAsync(CancellationToken ct = default) => UseAsync<ITunnelService, DashboardModel>(s => s.GetDashboardAsync(ct));
    public Task<IReadOnlyList<ProviderAccountModel>> GetAccountsAsync(CancellationToken ct = default) => UseAsync<IProviderAccountService, IReadOnlyList<ProviderAccountModel>>(s => s.GetListAsync(ct));
    public Task<ProviderAccountModel> SaveAccountAsync(SaveProviderAccountRequest request, CancellationToken ct = default) => UseAsync<IProviderAccountService, ProviderAccountModel>(s => s.SaveAsync(request, ct));
    public Task DeleteAccountAsync(Guid id, CancellationToken ct = default) => UseAsync<IProviderAccountService>(s => s.DeleteAsync(id, ct));
    public Task SyncAccountAsync(Guid id, CancellationToken ct = default) => UseAsync<IProviderAccountService>(s => s.SyncAsync(id, ct));
    public Task<IReadOnlyList<ProviderNodeModel>> GetNodesAsync(Guid id, CancellationToken ct = default) => UseAsync<IProviderAccountService, IReadOnlyList<ProviderNodeModel>>(s => s.GetNodesAsync(id, ct));
    public Task<IReadOnlyList<TunnelModel>> GetTunnelsAsync(CancellationToken ct = default) => UseAsync<ITunnelService, IReadOnlyList<TunnelModel>>(s => s.GetListAsync(ct));
    public Task<IReadOnlyList<string>> GetLocalAddressesAsync(CancellationToken ct = default) => UseAsync<ITunnelService, IReadOnlyList<string>>(s => s.GetLocalAddressSuggestionsAsync(ct));
    public Task<TunnelModel> SaveTunnelAsync(SaveTunnelRequest request, CancellationToken ct = default) => UseAsync<ITunnelService, TunnelModel>(s => s.SaveAsync(request, ct));
    public Task DeleteTunnelAsync(Guid id, CancellationToken ct = default) => UseAsync<ITunnelService>(s => s.DeleteAsync(id, ct));
    public Task StartTunnelAsync(Guid id, CancellationToken ct = default) => UseAsync<ITunnelService>(s => s.StartAsync(id, ct));
    public Task StopTunnelAsync(Guid id, CancellationToken ct = default) => UseAsync<ITunnelService>(s => s.StopAsync(id, ct));
    public Task<LocalEndpointTestResult> TestEndpointAsync(LocalEndpointTestRequest request, CancellationToken ct = default) => UseAsync<ITunnelService, LocalEndpointTestResult>(s => s.TestLocalEndpointAsync(request, ct));
    public Task<IReadOnlyList<TunnelRunModel>> GetRunsAsync(Guid? tunnelId = null, CancellationToken ct = default) => UseAsync<ITunnelService, IReadOnlyList<TunnelRunModel>>(s => s.GetRunsAsync(tunnelId, ct));
    public Task<LogTailModel> TailLogAsync(Guid id, long cursor = 0, CancellationToken ct = default) => UseAsync<ILogService, LogTailModel>(s => s.TailAsync(id, cursor, 300, ct));
    public Task ExportLogAsync(Guid id, Stream destination, CancellationToken ct = default) =>
        UseAsync<Frpm.Infrastructure.Services.LogFileExportService>(s => s.CopyAsync(id, destination, ct));
    public Task ValidateLogExportDestinationAsync(string? path) =>
        UseAsync<Frpm.Infrastructure.Services.LogFileExportService>(s => { s.ValidateDestination(path); return Task.CompletedTask; });
    public Task<(int Deleted, int Skipped)> CleanupLogsAsync(IReadOnlyCollection<Guid> excluded, CancellationToken ct = default) =>
        UseAsync<Frpm.Infrastructure.Services.LogCleanupService, (int Deleted, int Skipped)>(s => s.CleanupAsync(excluded, ct));
    public Task<IReadOnlyList<CliPackageModel>> GetPackagesAsync(CancellationToken ct = default) => UseAsync<ICliPackageService, IReadOnlyList<CliPackageModel>>(s => s.GetListAsync(ct));
    public Task<CliPackageModel> InstallCliAsync(InstallCliFromUrlRequest request, CancellationToken ct = default) => UseAsync<ICliPackageService, CliPackageModel>(s => s.InstallFromUrlAsync(request, ct));
    public Task<CliPackageModel> InstallCliFileAsync(ProviderType provider, string version, string fileName, Stream content, long length,
        string? sha256, bool replace, bool activate, CancellationToken ct = default) =>
        UseAsync<ICliPackageService, CliPackageModel>(s => s.UploadAsync(provider, version,
            new RemoteUploadFile(content, fileName, "application/octet-stream", length, true), sha256, replace, activate, ct));
    public Task ActivateCliAsync(Guid id, CancellationToken ct = default) => UseAsync<ICliPackageService>(s => s.ActivateAsync(id, true, ct));
    public Task DeleteCliAsync(Guid id, CancellationToken ct = default) => UseAsync<ICliPackageService>(s => s.DeleteAsync(id, ct));
    public Task<SystemSettingsModel> GetSettingsAsync(CancellationToken ct = default) => UseAsync<ISystemSettingsService, SystemSettingsModel>(s => s.GetAsync(ct));
    public Task<SystemSettingsModel> SaveSettingsAsync(int interval, CancellationToken ct = default) => UseAsync<ISystemSettingsService, SystemSettingsModel>(s => s.SaveAsync(new(interval), ct));
    public Task SyncAllAsync(CancellationToken ct = default) => UseAsync<ISystemSettingsService>(s => s.RequestProviderSyncAsync(ct));
    public Task<UserPreferencesModel> GetPreferencesAsync(CancellationToken ct = default) => UseAsync<IUserPreferencesService, UserPreferencesModel>(s => s.GetAsync(ct));
    public Task<UserPreferencesModel> SavePreferencesAsync(SaveUserPreferencesRequest request, CancellationToken ct = default) => UseAsync<IUserPreferencesService, UserPreferencesModel>(s => s.SaveAsync(request, ct));

    public async Task SetAllTunnelsRunningAsync(bool running, CancellationToken ct = default)
    {
        var errors = new List<string>();
        foreach (var tunnel in await GetTunnelsAsync(ct))
        {
            try
            {
                if (running && tunnel.RuntimeState is not (TunnelRuntimeState.Running or TunnelRuntimeState.Starting))
                    await StartTunnelAsync(tunnel.Id, ct);
                else if (!running && tunnel.RuntimeState is not (TunnelRuntimeState.Stopped or TunnelRuntimeState.Stopping))
                    await StopTunnelAsync(tunnel.Id, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"{tunnel.Name}：{ex.Message}");
            }
        }
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
    }
}
