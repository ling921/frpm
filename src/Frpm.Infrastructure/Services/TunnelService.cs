using Frpm.Abstractions.Models;
using Frpm.Abstractions.Services;
using Frpm.Data;
using Frpm.Domain.Entities;
using Frpm.Domain.Enums;
using Frpm.Infrastructure.Concurrency;
using Frpm.Infrastructure.Providers;
using Frpm.Infrastructure.Runtime;
using Frpm.Infrastructure.Security;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace Frpm.Infrastructure.Services;

[ScopedService(typeof(ITunnelService))]
public sealed class TunnelService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IFrpProviderFactory providerFactory,
    CredentialProtector credentialProtector,
    ITunnelProcessSupervisor supervisor,
    OperationLock operationLock,
    IMapper mapper,
    ILogger<TunnelService> logger) : ITunnelService
{
    public async Task<DashboardModel> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return new(
            await db.ProviderAccounts.CountAsync(cancellationToken),
            await db.Tunnels.CountAsync(x => x.DeletedAt == null, cancellationToken),
            await db.Tunnels.CountAsync(x => x.RuntimeState == TunnelRuntimeState.Running, cancellationToken),
            await db.Tunnels.CountAsync(x => x.RuntimeState == TunnelRuntimeState.Failed, cancellationToken),
            await db.Tunnels.CountAsync(x => x.RemoteState == TunnelRemoteState.RemoteMissing, cancellationToken));
    }

    public async Task<IReadOnlyList<TunnelModel>> GetListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var tunnels = await db.Tunnels.AsNoTracking().Include(x => x.ProviderAccount).Where(x => x.DeletedAt == null)
            .OrderBy(x => x.ProviderAccount.DisplayName).ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
        return tunnels.Select(tunnel => mapper.Map<TunnelModel>(tunnel) with
        {
            ProviderDomain = ReadPayloadValue(tunnel.ProviderPayloadJson, "domain"),
            NodeAddress = ReadPayloadValue(tunnel.ProviderPayloadJson, "node_address")
        }).ToList();
    }

    public Task<IReadOnlyList<string>> GetLocalAddressSuggestionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var suggestions = new List<string> { "127.0.0.1", "0.0.0.0", "::1", "::" };
        try
        {
            var interfaceAddresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(network => network.OperationalStatus == OperationalStatus.Up)
                .SelectMany(network => network.GetIPProperties().UnicastAddresses)
                .Select(address => address.Address)
                .Where(address => address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                .Where(address => !IPAddress.IsLoopback(address) && !address.IsIPv6LinkLocal)
                .Select(address => address.ToString())
                .OrderBy(address => address.Contains(':'))
                .ThenBy(address => address, StringComparer.OrdinalIgnoreCase);
            suggestions.AddRange(interfaceAddresses);
        }
        catch (NetworkInformationException)
        {
            // Wildcard and loopback suggestions remain available when interface enumeration is unavailable.
        }

        return Task.FromResult<IReadOnlyList<string>>(suggestions.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    public async Task<TunnelModel> SaveAsync(SaveTunnelRequest request, CancellationToken cancellationToken = default)
    {
        await using var gate = await operationLock.AcquireAsync($"account:{request.ProviderAccountId}:save", cancellationToken);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var account = await db.ProviderAccounts.SingleOrDefaultAsync(x => x.Id == request.ProviderAccountId, cancellationToken)
            ?? throw new KeyNotFoundException("供应商账号不存在。");
        var provider = providerFactory.Get(account.ProviderType);
        NormalizeAndValidateRequest(request, account.ProviderType);
        var credentials = credentialProtector.Unprotect(account.ProtectedCredentials) with { AccountId = account.Id };
        if (request.TunnelId is null && provider.Capabilities.HasNodes && string.IsNullOrWhiteSpace(request.NodeId))
        {
            throw new InvalidOperationException("创建隧道时必须选择节点。");
        }

        ProviderTunnel remote;
        Tunnel entity;
        if (request.TunnelId is { } tunnelId)
        {
            entity = await db.Tunnels.SingleOrDefaultAsync(x => x.Id == tunnelId && x.ProviderAccountId == account.Id, cancellationToken)
                ?? throw new KeyNotFoundException("隧道不存在。");
            request.NodeId = entity.NodeId;
            remote = await provider.UpdateTunnelAsync(account.ApiBaseUrl, credentials, ToProvider(entity), request, cancellationToken);
        }
        else
        {
            remote = await provider.CreateTunnelAsync(account.ApiBaseUrl, credentials, request, cancellationToken);
            entity = await db.Tunnels.SingleOrDefaultAsync(x => x.ProviderAccountId == account.Id && x.RemoteId == remote.RemoteId, cancellationToken)
                ?? new Tunnel { ProviderAccountId = account.Id, RemoteId = remote.RemoteId, Name = remote.Name, Type = remote.Type };
            if (db.Entry(entity).State == EntityState.Detached) db.Tunnels.Add(entity);
        }
        remote = await EnrichNodeAsync(provider, account.ApiBaseUrl, credentials, remote, cancellationToken);
        Apply(entity, remote);
        await db.SaveChangesAsync(cancellationToken);
        entity.ProviderAccount = account;
        return mapper.Map<TunnelModel>(entity);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var gate = await operationLock.AcquireAsync($"tunnel:{id}:delete", cancellationToken);
        await supervisor.StopAsync(id, "删除隧道。", cancellationToken);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var tunnel = await db.Tunnels.Include(x => x.ProviderAccount).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("隧道不存在。");
        var provider = providerFactory.Get(tunnel.ProviderAccount.ProviderType);
        await provider.DeleteTunnelAsync(tunnel.ProviderAccount.ApiBaseUrl, credentialProtector.Unprotect(tunnel.ProviderAccount.ProtectedCredentials) with { AccountId = tunnel.ProviderAccount.Id }, ToProvider(tunnel), cancellationToken);
        tunnel.RemoteState = TunnelRemoteState.Deleted;
        tunnel.DeletedAt = DateTimeOffset.UtcNow;
        tunnel.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task StartAsync(Guid id, CancellationToken cancellationToken = default) => supervisor.StartAsync(id, cancellationToken);
    public Task StopAsync(Guid id, CancellationToken cancellationToken = default) => supervisor.StopAsync(id, "管理员手动停止。", cancellationToken);

    public Task<LocalEndpointTestResult> TestLocalEndpointAsync(LocalEndpointTestRequest request, CancellationToken cancellationToken = default) =>
        LocalEndpointProbe.TestAsync(request, cancellationToken);

    public async Task<IReadOnlyList<TunnelRunModel>> GetRunsAsync(Guid? tunnelId = null, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.TunnelRuns.AsNoTracking().Include(x => x.Tunnel).Include(x => x.CliPackage).AsQueryable();
        if (tunnelId is not null) query = query.Where(x => x.TunnelId == tunnelId);
        return await query.OrderByDescending(x => x.StartedAt).Take(200)
            .ProjectToType<TunnelRunModel>()
            .ToListAsync(cancellationToken);
    }

    private static ProviderTunnel ToProvider(Tunnel x) => new(x.RemoteId, x.Name, x.Remark, x.Type, x.NodeId, x.NodeName,
        x.LocalAddress, x.LocalPort, x.RemoteAddress, x.ProviderStatus, x.ProviderPayloadJson);

    private static string? ReadPayloadValue(string payload, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty(propertyName, out var value)) return null;
            var result = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
            return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<ProviderTunnel> EnrichNodeAsync(
        IFrpProvider provider,
        string apiBaseUrl,
        ProviderCredentials credentials,
        ProviderTunnel tunnel,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tunnel.NodeId) || !string.IsNullOrWhiteSpace(tunnel.NodeName))
        {
            return tunnel;
        }

        try
        {
            var nodes = await provider.GetNodesAsync(apiBaseUrl, credentials, cancellationToken);
            var nodeName = nodes.FirstOrDefault(node => string.Equals(node.Id, tunnel.NodeId, StringComparison.Ordinal))?.Name;
            return string.IsNullOrWhiteSpace(nodeName) ? tunnel : tunnel with { NodeName = nodeName };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "无法为隧道 {RemoteId} 补充节点名称，保存将继续。", tunnel.RemoteId);
            return tunnel;
        }
    }

    private static void Apply(Tunnel entity, ProviderTunnel remote)
    {
        entity.Name = remote.Name;
        entity.Remark = remote.Remark;
        entity.Type = remote.Type;
        entity.NodeId = remote.NodeId;
        entity.NodeName = remote.NodeName;
        entity.LocalAddress = remote.LocalAddress;
        entity.LocalPort = remote.LocalPort;
        entity.RemoteAddress = remote.RemoteAddress;
        entity.ProviderStatus = remote.ProviderStatus;
        entity.ProviderPayloadJson = remote.PayloadJson;
        entity.RemoteState = remote.ProviderStatus?.ToLowerInvariant() switch
        {
            "active" or "online" or "running" => TunnelRemoteState.Active,
            "inactive" or "offline" or "stopped" => TunnelRemoteState.Inactive,
            _ => TunnelRemoteState.Unknown
        };
        entity.LastSeenRemoteAt = entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.DeletedAt = null;
    }

    private static void NormalizeAndValidateRequest(SaveTunnelRequest request, ProviderType providerType)
    {
        request.Type = (request.Type ?? "").Trim().ToLowerInvariant();
        if (!TunnelInputRules.TypesFor(providerType).Contains(request.Type, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("当前供应商不支持所选隧道类型。");
        }

        if (TunnelInputRules.RequiresLocalEndpoint(providerType, request.Type))
        {
            request.LocalAddress = request.LocalAddress?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(request.LocalAddress)) throw new InvalidOperationException("本地地址不能为空。");
            if (request.LocalPort is not (> 0 and <= 65_535)) throw new InvalidOperationException("本地端口必须在 1–65535 之间。");
        }
        else
        {
            request.LocalAddress = "";
            request.LocalPort = null;
        }

        if (TunnelInputRules.IsDomainType(request.Type))
        {
            var domains = TunnelInputRules.ParseDomains(request.CustomDomain);
            if (domains.Count == 0) throw new InvalidOperationException("HTTP(S) 隧道至少需要一个绑定域名。");
            var maximum = TunnelInputRules.MaximumDomains(providerType);
            if (domains.Count > maximum)
            {
                throw new InvalidOperationException($"当前供应商最多支持 {maximum} 个绑定域名。");
            }
            foreach (var domain in domains)
            {
                if (domain.Contains("://", StringComparison.Ordinal)
                    || domain.Contains('/')
                    || domain.Contains(':')
                    || Uri.CheckHostName(domain) != UriHostNameType.Dns)
                {
                    throw new InvalidOperationException($"绑定域名“{domain}”格式无效，请勿包含协议、端口或路径。");
                }
            }
            request.CustomDomain = string.Join(", ", domains);
            request.RemotePort = null;
        }
        else
        {
            request.CustomDomain = null;
            if (TunnelInputRules.UsesRemotePort(providerType, request.Type)
                && request.RemotePort is not null and not (>= 0 and <= 65_535))
            {
                throw new InvalidOperationException("远程端口必须在 0–65535 之间。");
            }
            if (!TunnelInputRules.UsesRemotePort(providerType, request.Type)) request.RemotePort = null;
        }
    }
}
