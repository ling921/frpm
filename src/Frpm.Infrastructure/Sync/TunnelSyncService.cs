using Frpm.Data;
using Frpm.Domain.Entities;
using Frpm.Domain.Enums;
using Frpm.Infrastructure.Providers;
using Frpm.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Frpm.Infrastructure.Sync;

[ScopedService]
public sealed class TunnelSyncService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IFrpProviderFactory providerFactory,
    CredentialProtector credentialProtector,
    ILogger<TunnelSyncService> logger)
{
    public async Task SyncAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var account = await db.ProviderAccounts.SingleOrDefaultAsync(x => x.Id == accountId, cancellationToken)
            ?? throw new KeyNotFoundException("供应商账号不存在。");
        if (!account.Enabled) return;

        var provider = providerFactory.Get(account.ProviderType);
        var credentials = credentialProtector.Unprotect(account.ProtectedCredentials) with { AccountId = account.Id };
        var remoteTunnels = new List<ProviderTunnel>();

        try
        {
            var page = 1;
            while (true)
            {
                var result = await provider.GetTunnelsAsync(account.ApiBaseUrl, credentials, page, cancellationToken);
                remoteTunnels.AddRange(result.Items);
                if (result.Page >= result.TotalPages) break;
                page++;
            }

            if (remoteTunnels.Any(tunnel => !string.IsNullOrWhiteSpace(tunnel.NodeId) && string.IsNullOrWhiteSpace(tunnel.NodeName)))
            {
                try
                {
                    var nodeNames = (await provider.GetNodesAsync(account.ApiBaseUrl, credentials, cancellationToken))
                        .ToDictionary(node => node.Id, node => node.Name, StringComparer.Ordinal);
                    for (var index = 0; index < remoteTunnels.Count; index++)
                    {
                        var tunnel = remoteTunnels[index];
                        if (string.IsNullOrWhiteSpace(tunnel.NodeName)
                            && tunnel.NodeId is { } nodeId
                            && nodeNames.TryGetValue(nodeId, out var nodeName))
                        {
                            remoteTunnels[index] = tunnel with { NodeName = nodeName };
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "无法为供应商账号 {AccountId} 补充节点名称，隧道同步将继续。", accountId);
                }
            }

            var now = DateTimeOffset.UtcNow;
            var existing = await db.Tunnels.Where(x => x.ProviderAccountId == accountId).ToDictionaryAsync(x => x.RemoteId, cancellationToken);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var remote in remoteTunnels)
            {
                seen.Add(remote.RemoteId);
                if (!existing.TryGetValue(remote.RemoteId, out var tunnel))
                {
                    tunnel = new Tunnel { ProviderAccountId = accountId, RemoteId = remote.RemoteId, Name = remote.Name, Type = remote.Type };
                    db.Tunnels.Add(tunnel);
                }
                Apply(tunnel, remote, now);
            }

            foreach (var missing in existing.Values.Where(x => x.DeletedAt is null && !seen.Contains(x.RemoteId)))
            {
                missing.RemoteState = TunnelRemoteState.RemoteMissing;
                missing.RuntimeMessage = missing.RuntimeState == TunnelRuntimeState.Running ? "远端隧道已不存在，请停止本地进程。" : missing.RuntimeMessage;
                missing.UpdatedAt = now;
            }

            account.LastSyncAt = now;
            account.LastSyncSucceeded = true;
            account.LastSyncMessage = $"同步完成，共 {remoteTunnels.Count} 条隧道。";
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "同步供应商账号 {AccountId} 失败", accountId);
            db.ChangeTracker.Clear();
            var failedAccount = await db.ProviderAccounts.SingleAsync(x => x.Id == accountId, cancellationToken);
            failedAccount.LastSyncAt = DateTimeOffset.UtcNow;
            failedAccount.LastSyncSucceeded = false;
            failedAccount.LastSyncMessage = ex.Message;
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private static void Apply(Tunnel tunnel, ProviderTunnel remote, DateTimeOffset now)
    {
        tunnel.Name = remote.Name;
        tunnel.Remark = remote.Remark;
        tunnel.Type = remote.Type;
        tunnel.NodeId = remote.NodeId;
        tunnel.NodeName = remote.NodeName;
        tunnel.LocalAddress = remote.LocalAddress;
        tunnel.LocalPort = remote.LocalPort;
        tunnel.RemoteAddress = remote.RemoteAddress;
        tunnel.ProviderStatus = remote.ProviderStatus;
        tunnel.ProviderPayloadJson = remote.PayloadJson;
        tunnel.RemoteState = Normalize(remote.ProviderStatus);
        tunnel.LastSeenRemoteAt = now;
        tunnel.UpdatedAt = now;
        tunnel.DeletedAt = null;
    }

    private static TunnelRemoteState Normalize(string? status) => status?.ToLowerInvariant() switch
    {
        "active" or "online" or "running" => TunnelRemoteState.Active,
        "inactive" or "offline" or "stopped" => TunnelRemoteState.Inactive,
        _ => TunnelRemoteState.Unknown
    };
}
