using Frpm.Abstractions.Models;
using Frpm.Abstractions.Services;
using Frpm.Data;
using Frpm.Domain.Entities;
using Frpm.Domain.Enums;
using Frpm.Infrastructure.Providers;
using Frpm.Infrastructure.Runtime;
using Frpm.Infrastructure.Security;
using Frpm.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;

namespace Frpm.Infrastructure.Services;

[ScopedService(typeof(IProviderAccountService))]
public sealed class ProviderAccountService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IFrpProviderFactory providerFactory,
    CredentialProtector credentialProtector,
    TunnelSyncService syncService,
    ITunnelProcessSupervisor supervisor) : IProviderAccountService
{
    public async Task<IReadOnlyList<ProviderAccountModel>> GetListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var accounts = await db.ProviderAccounts.AsNoTracking().Include(x => x.Tunnels).OrderBy(x => x.DisplayName).ToListAsync(cancellationToken);
        return accounts.Select(Map).ToList();
    }

    public async Task<ProviderAccountModel> SaveAsync(SaveProviderAccountRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName)) throw new InvalidOperationException("账号名称不能为空。");
        var provider = providerFactory.Get(request.ProviderType);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        ProviderAccount account;
        ProviderCredentials credentials;
        if (request.Id is { } id)
        {
            account = await db.ProviderAccounts.Include(x => x.Tunnels).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("供应商账号不存在。");
            var current = credentialProtector.Unprotect(account.ProtectedCredentials);
            credentials = new(
                string.IsNullOrWhiteSpace(request.AccessToken) ? current.AccessToken : request.AccessToken,
                string.IsNullOrWhiteSpace(request.RefreshToken) ? current.RefreshToken : request.RefreshToken,
                string.IsNullOrWhiteSpace(request.ClientId) ? current.ClientId : request.ClientId,
                string.IsNullOrWhiteSpace(request.ClientSecret) ? current.ClientSecret : request.ClientSecret,
                account.Id);
        }
        else
        {
            account = new ProviderAccount
            {
                ProviderType = request.ProviderType,
                DisplayName = request.DisplayName.Trim(),
                ApiBaseUrl = BuiltInProviderCatalog.Get(request.ProviderType).DefaultApiBaseUrl,
                ProtectedCredentials = string.Empty
            };
            credentials = new(request.AccessToken, request.RefreshToken, request.ClientId, request.ClientSecret);
            db.ProviderAccounts.Add(account);
        }

        var baseUrl = string.IsNullOrWhiteSpace(request.ApiBaseUrl)
            ? BuiltInProviderCatalog.Get(request.ProviderType).DefaultApiBaseUrl
            : request.ApiBaseUrl.Trim();
        await provider.ValidateAccountAsync(baseUrl, credentials, cancellationToken);
        account.ProviderType = request.ProviderType;
        account.DisplayName = request.DisplayName.Trim();
        account.ApiBaseUrl = baseUrl.TrimEnd('/');
        account.Enabled = request.Enabled;
        account.ProtectedCredentials = credentialProtector.Protect(credentials with { AccountId = null });
        await db.SaveChangesAsync(cancellationToken);
        await syncService.SyncAsync(account.Id, cancellationToken);

        await db.Entry(account).Collection(x => x.Tunnels).LoadAsync(cancellationToken);
        return Map(account);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var account = await db.ProviderAccounts.Include(x => x.Tunnels).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("供应商账号不存在。");
        foreach (var tunnel in account.Tunnels.Where(x => x.RuntimeState is TunnelRuntimeState.Running or TunnelRuntimeState.Starting))
            await supervisor.StopAsync(tunnel.Id, "删除供应商账号。", cancellationToken);
        db.ProviderAccounts.Remove(account);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task SyncAsync(Guid id, CancellationToken cancellationToken = default) => syncService.SyncAsync(id, cancellationToken);

    public async Task<IReadOnlyList<ProviderNodeModel>> GetNodesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var account = await db.ProviderAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("供应商账号不存在。");
        return await providerFactory.Get(account.ProviderType).GetNodesAsync(account.ApiBaseUrl, credentialProtector.Unprotect(account.ProtectedCredentials) with { AccountId = account.Id }, cancellationToken);
    }

    private ProviderAccountModel Map(ProviderAccount account) => new(
        account.Id, account.ProviderType, account.DisplayName, account.ApiBaseUrl, account.Enabled,
        account.LastSyncAt, account.LastSyncSucceeded, account.LastSyncMessage,
        account.Tunnels.Count(x => x.DeletedAt is null), providerFactory.Get(account.ProviderType).Capabilities);

}
