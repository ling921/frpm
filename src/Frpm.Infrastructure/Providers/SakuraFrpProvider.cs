using Frpm.Abstractions.Models;
using Frpm.Domain.Enums;
using System.Text.Json;

namespace Frpm.Infrastructure.Providers;

[SingletonService(typeof(IFrpProvider), ServiceKey = nameof(ProviderType.SakuraFrp))]
public sealed class SakuraFrpProvider(IHttpClientFactory httpClientFactory) : HttpProviderBase(httpClientFactory), IFrpProvider
{
    public ProviderType Type => ProviderType.SakuraFrp;
    public ProviderCapabilitiesModel Capabilities => BuiltInProviderCatalog.Get(Type).Capabilities;

    public async Task ValidateAccountAsync(string apiBaseUrl, ProviderCredentials credentials, CancellationToken cancellationToken)
    {
        using var _ = await SendAsync(apiBaseUrl, credentials, HttpMethod.Get, "user/info", null, cancellationToken);
    }

    public async Task<ProviderAccountMetrics?> GetAccountMetricsAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        CancellationToken cancellationToken)
    {
        try
        {
            using var doc = await SendAsync(apiBaseUrl, credentials, HttpMethod.Get, "user/info", null, cancellationToken);
            var data = Data(doc.RootElement);
            var trafficKib = Long(data, "traffic");
            var trafficBytes = trafficKib is > 0 ? trafficKib * 1024 : null;
            var group = String(data, "group");
            return new ProviderAccountMetrics(trafficBytes, null, null, group);
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<ProviderNodeModel>> GetNodesAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        CancellationToken cancellationToken)
    {
        var statsMap = new Dictionary<string, (int? Load, bool? Online)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var statsDoc = await SendAsync(apiBaseUrl, credentials, HttpMethod.Get, "node/stats", null, cancellationToken);
            var statsData = Data(statsDoc.RootElement);
            if (statsData.TryGetProperty("nodes", out var statsNodes) && statsNodes.ValueKind == JsonValueKind.Array)
            {
                foreach (var sn in statsNodes.EnumerateArray())
                {
                    var snId = String(sn, "id");
                    if (!string.IsNullOrWhiteSpace(snId))
                    {
                        var onlineVal = Integer(sn, "online");
                        var loadVal = Integer(sn, "load");
                        statsMap[snId] = (loadVal, onlineVal is >= 0);
                    }
                }
            }
        }
        catch
        {
            // Ignore stats failure
        }

        using var document = await SendAsync(apiBaseUrl, credentials, HttpMethod.Get, "nodes", null, cancellationToken);
        var root = Data(document.RootElement);
        var list = root.ValueKind == JsonValueKind.Object
            ? root.EnumerateObject().Select(property => MapNode(property.Name, property.Value, statsMap)).ToList()
            : root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray().Select(node => MapNode(String(node, "id") ?? "", node, statsMap)).ToList()
                : [];
        return list;
    }

    public async Task<ProviderTunnelPage> GetTunnelsAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        int page,
        CancellationToken cancellationToken)
    {
        if (page > 1)
        {
            return new([], page, 1);
        }

        using var document = await SendAsync(apiBaseUrl, credentials, HttpMethod.Get, "tunnels", null, cancellationToken);
        var root = Data(document.RootElement);
        var items = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().Select(MapTunnel).Where(x => x is not null).Cast<ProviderTunnel>().ToList()
            : [];
        return new(items, 1, 1);
    }

    public async Task<ProviderTunnel> CreateTunnelAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        SaveTunnelRequest request,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            name = request.Name,
            type = request.Type,
            node = int.TryParse(request.NodeId, out var node) ? node : 0,
            note = request.Remark,
            local_ip = request.LocalAddress,
            local_port = request.LocalPort,
            remote = request.CustomDomain ?? request.RemotePort?.ToString(),
            extra = string.Join('\n', request.AdvancedOptions.Where(x => x.Value is not null).Select(x => $"{x.Key} = {x.Value}"))
        };
        using var document = await SendAsync(apiBaseUrl, credentials, HttpMethod.Post, "tunnels", body, cancellationToken);
        var data = Data(document.RootElement);
        return FromRequest(request, String(data, "id") ?? throw new InvalidOperationException("供应商未返回隧道 ID。"), String(data, "name") ?? request.Name, data.GetRawText());
    }

    public async Task<ProviderTunnel> UpdateTunnelAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        ProviderTunnel current,
        SaveTunnelRequest request,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            id = int.TryParse(current.RemoteId, out var id) ? id : 0,
            note = request.Remark,
            local_ip = request.LocalAddress,
            local_port = request.LocalPort,
            extra = string.Join('\n', request.AdvancedOptions.Where(x => x.Value is not null).Select(x => $"{x.Key}={x.Value}"))
        };
        using var _ = await SendAsync(apiBaseUrl, credentials, HttpMethod.Post, "tunnel/edit", body, cancellationToken);
        return FromRequest(request, current.RemoteId, current.Name, current.PayloadJson) with
        {
            NodeId = current.NodeId,
            NodeName = current.NodeName,
            ProviderStatus = current.ProviderStatus
        };
    }

    public async Task DeleteTunnelAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        ProviderTunnel tunnel,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tunnel.RemoteId)) return;
        try
        {
            using var _ = await SendAsync(apiBaseUrl, credentials, HttpMethod.Post, "tunnel/delete", new { ids = tunnel.RemoteId }, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("404") || ex.Message.Contains("不存在") || ex.Message.Contains("not found"))
        {
            // 远端隧道已不存在，视为已删除（幂等）
        }
    }

    public Task<LaunchSpecification> PrepareLaunchAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        ProviderTunnel tunnel,
        string executablePath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var token = credentials.AccessToken ?? throw new InvalidOperationException("访问令牌不能为空。");
        LaunchSpecification specification = new(executablePath, workingDirectory, ["-f", $"{token}:{tunnel.RemoteId}"], new Dictionary<string, string?>(), [token]);
        return Task.FromResult(specification);
    }

    private static ProviderTunnel? MapTunnel(JsonElement value)
    {
        var id = String(value, "id", "tunnel_id");
        if (string.IsNullOrWhiteSpace(id)) return null;
        var remote = new[] { String(value, "remote"), String(value, "remote_port"), String(value, "domain") }
            .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
        var providerStatus = value.TryGetProperty("online", out var online) && online.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? online.GetBoolean() ? "active" : "inactive"
            : null;
        return new(id, String(value, "name") ?? id, String(value, "note", "remark"), String(value, "type") ?? "tcp",
            String(value, "node", "node_id"), String(value, "node_name"), String(value, "local_ip"), Integer(value, "local_port"),
            remote, providerStatus, value.GetRawText());
    }

    private static ProviderTunnel FromRequest(SaveTunnelRequest request, string id, string name, string payload) =>
        new(id, name, request.Remark, request.Type, request.NodeId, null, request.LocalAddress, request.LocalPort,
            request.CustomDomain ?? request.RemotePort?.ToString(), "inactive", payload);

    private static ProviderNodeModel MapNode(string id, JsonElement node, Dictionary<string, (int? Load, bool? Online)> statsMap)
    {
        var flag = Integer(node, "flag") ?? 0;
        var canCreate = (flag & (1 << 2)) != 0;
        var offlineFlag = (flag & (1 << 9)) != 0;
        var types = new List<string> { "TCP" };
        if ((flag & (1 << 5)) != 0) types.Add("UDP");
        if ((flag & 0b11) == 0b11) types.AddRange(["HTTP", "HTTPS"]);

        statsMap.TryGetValue(id, out var stat);
        var isOnline = (stat.Online ?? !offlineFlag) && !offlineFlag;
        var name = String(node, "name") ?? "未命名节点";
        var isVip = Boolean(node, "vip") == true;
        var regionGroup = NodeRegionClassifier.Classify(null, null, name);

        return new(
            id,
            name,
            String(node, "host", "address"),
            canCreate && isOnline,
            Region: null,
            Bandwidth: null,
            AllowedTypes: string.Join(" / ", types),
            PortRange: null,
            Description: String(node, "description"),
            LoadPercent: stat.Load,
            IsOnline: isOnline,
            NeedKyc: false,
            NeedBeian: false,
            IsVip: isVip,
            TrafficRatio: null,
            RegionGroup: regionGroup);
    }
}
