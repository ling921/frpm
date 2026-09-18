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

    public async Task<IReadOnlyList<ProviderNodeModel>> GetNodesAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        CancellationToken cancellationToken)
    {
        using var document = await SendAsync(apiBaseUrl, credentials, HttpMethod.Get, "nodes", null, cancellationToken);
        var root = Data(document.RootElement);
        if (root.ValueKind == JsonValueKind.Object)
        {
            return root.EnumerateObject().Select(property => MapNode(property.Name, property.Value)).ToList();
        }

        return root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().Select(node => MapNode(String(node, "id") ?? "", node)).ToList()
            : [];
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
        using var _ = await SendAsync(apiBaseUrl, credentials, HttpMethod.Post, "tunnel/delete", new { ids = tunnel.RemoteId }, cancellationToken);
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

    private static ProviderNodeModel MapNode(string id, JsonElement node)
    {
        var flag = Integer(node, "flag") ?? 0;
        var canCreate = (flag & (1 << 2)) != 0;
        var offline = (flag & (1 << 9)) != 0;
        var types = new List<string> { "TCP" };
        if ((flag & (1 << 5)) != 0) types.Add("UDP");
        if ((flag & 0b11) == 0b11) types.AddRange(["HTTP", "HTTPS"]);
        return new(
            id,
            String(node, "name") ?? "未命名节点",
            String(node, "host", "address"),
            canCreate && !offline,
            AllowedTypes: string.Join(" / ", types),
            Description: String(node, "description"));
    }
}
