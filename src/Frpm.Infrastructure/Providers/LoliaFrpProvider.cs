using Frpm.Abstractions.Models;
using Frpm.Domain.Enums;
using Frpm.Infrastructure.Services;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Frpm.Infrastructure.Providers;

[SingletonService(typeof(IFrpProvider), ServiceKey = nameof(ProviderType.LoliaFrp))]
public sealed class LoliaFrpProvider(
    IHttpClientFactory httpClientFactory,
    IProviderCredentialStore credentialStore) : HttpProviderBase(httpClientFactory), IFrpProvider
{
    private readonly ConcurrentDictionary<Guid, CachedCredentials> _credentialCache = new();
    public ProviderType Type => ProviderType.LoliaFrp;
    public ProviderCapabilitiesModel Capabilities => BuiltInProviderCatalog.Get(Type).Capabilities;

    public async Task ValidateAccountAsync(string apiBaseUrl, ProviderCredentials credentials, CancellationToken cancellationToken)
    {
        credentials = await RefreshAsync(apiBaseUrl, credentials, cancellationToken);
        using var _ = await SendAsync(apiBaseUrl, credentials, HttpMethod.Get, "user/info", null, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderNodeModel>> GetNodesAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        CancellationToken cancellationToken)
    {
        credentials = await RefreshAsync(apiBaseUrl, credentials, cancellationToken);
        using var document = await SendAsync(apiBaseUrl, credentials, HttpMethod.Post, "user/nodes", new { }, cancellationToken);
        var data = Data(document.RootElement);
        var nodes = data.TryGetProperty("nodes", out var array) ? array : data;
        if (nodes.ValueKind != JsonValueKind.Array) return [];
        return nodes.EnumerateArray().Select(node => new ProviderNodeModel(
            String(node, "id") ?? "", String(node, "name") ?? "未命名节点", String(node, "ip_address", "address", "host"),
            !string.Equals(String(node, "status"), "offline", StringComparison.OrdinalIgnoreCase),
            String(node, "region", "location"),
            Bandwidth(node),
            StringList(node, "supported_protocols") ?? String(node, "allow_type", "allowType", "type"),
            String(node, "allow_port", "allowPort", "port_range"),
            String(node, "description"))).ToList();
    }

    public async Task<ProviderTunnelPage> GetTunnelsAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        int page,
        CancellationToken cancellationToken)
    {
        credentials = await RefreshAsync(apiBaseUrl, credentials, cancellationToken);
        using var document = await SendAsync(apiBaseUrl, credentials, HttpMethod.Get, $"user/tunnel?page={page}&limit=100", null, cancellationToken);
        var data = Data(document.RootElement);
        var list = data.TryGetProperty("list", out var array) ? array : default;
        var items = list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(MapTunnel).Where(x => x is not null).Cast<ProviderTunnel>().ToList()
            : [];
        return new(items, Integer(data, "page") ?? page, Integer(data, "total_page") ?? page);
    }

    public async Task<ProviderTunnel> CreateTunnelAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        SaveTunnelRequest request,
        CancellationToken cancellationToken)
    {
        credentials = await RefreshAsync(apiBaseUrl, credentials, cancellationToken);
        var body = new
        {
            node_id = int.TryParse(request.NodeId, out var node) ? node : 0,
            type = request.Type,
            local_ip = request.LocalAddress,
            local_port = request.LocalPort,
            remote_port = request.RemotePort ?? 0,
            custom_domain = request.CustomDomain ?? "",
            remark = request.Remark ?? request.Name
        };
        using var document = await SendAsync(apiBaseUrl, credentials, HttpMethod.Post, "user/tunnel", body, cancellationToken);
        var data = Data(document.RootElement);
        var id = String(data, "id", "name") ?? throw new InvalidOperationException("供应商未返回隧道标识。");
        return FromRequest(request, id, String(data, "name") ?? id, data.GetRawText());
    }

    public async Task<ProviderTunnel> UpdateTunnelAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        ProviderTunnel current,
        SaveTunnelRequest request,
        CancellationToken cancellationToken)
    {
        credentials = await RefreshAsync(apiBaseUrl, credentials, cancellationToken);
        var body = new
        {
            local_ip = request.LocalAddress,
            local_port = request.LocalPort,
            custom_domain = request.CustomDomain,
            remark = request.Remark,
            config = request.AdvancedOptions
        };
        using var document = await SendAsync(apiBaseUrl, credentials, HttpMethod.Put, $"user/tunnel/{Uri.EscapeDataString(current.Name)}", body, cancellationToken);
        return FromRequest(request, current.RemoteId, current.Name, Data(document.RootElement).GetRawText()) with
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
        credentials = await RefreshAsync(apiBaseUrl, credentials, cancellationToken);
        using var _ = await SendAsync(apiBaseUrl, credentials, HttpMethod.Delete, $"user/tunnel/{Uri.EscapeDataString(tunnel.Name)}", null, cancellationToken);
    }

    public async Task<LaunchSpecification> PrepareLaunchAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        ProviderTunnel tunnel,
        string executablePath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        credentials = await RefreshAsync(apiBaseUrl, credentials, cancellationToken);
        using var document = await SendAsync(apiBaseUrl, credentials, HttpMethod.Get, $"user/tunnel/{Uri.EscapeDataString(tunnel.Name)}", null, cancellationToken);
        var detail = Data(document.RootElement);
        var tunnelId = String(detail, "id") ?? tunnel.RemoteId;
        var tunnelToken = String(detail, "tunnel_token") ?? throw new InvalidOperationException("供应商未返回隧道启动 Token。");
        if (string.IsNullOrWhiteSpace(tunnelId)) throw new InvalidOperationException("供应商未返回隧道标识。");
        var launchToken = $"{tunnelId}:{tunnelToken}";
        return new(
            executablePath,
            workingDirectory,
            ["-t", launchToken],
            new Dictionary<string, string?>
            {
                ["CLICOLOR_FORCE"] = "1",
                ["TERM"] = "xterm-256color"
            },
            [launchToken, tunnelToken]);
    }

    private static ProviderTunnel? MapTunnel(JsonElement value)
    {
        var remoteId = String(value, "id", "name");
        var name = String(value, "name") ?? remoteId;
        if (string.IsNullOrWhiteSpace(remoteId) || string.IsNullOrWhiteSpace(name)) return null;
        var customDomain = String(value, "custom_domain");
        var remotePort = String(value, "remote_port");
        return new(remoteId, name, String(value, "remark"), String(value, "type") ?? "tcp", String(value, "node_id"),
            String(value, "node_name"), String(value, "local_ip"), Integer(value, "local_port"),
            string.IsNullOrWhiteSpace(customDomain) ? remotePort : customDomain, String(value, "status"), value.GetRawText());
    }

    private static ProviderTunnel FromRequest(SaveTunnelRequest request, string id, string name, string payload) =>
        new(id, name, request.Remark, request.Type, request.NodeId, null, request.LocalAddress, request.LocalPort,
            request.CustomDomain ?? request.RemotePort?.ToString(), "inactive", payload);

    private static string? Bandwidth(JsonElement node)
    {
        var value = Integer(node, "bandwidth");
        return value is > 0 ? $"{value} Mbps" : null;
    }

    private static string? StringList(JsonElement node, string name)
    {
        if (!node.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item));
        var result = string.Join(" / ", items!);
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private async Task<ProviderCredentials> RefreshAsync(string apiBaseUrl, ProviderCredentials credentials, CancellationToken cancellationToken)
    {
        if (credentials.AccountId is not { } accountId || string.IsNullOrWhiteSpace(credentials.RefreshToken) || string.IsNullOrWhiteSpace(credentials.ClientId)) return credentials;
        if (_credentialCache.TryGetValue(accountId, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return cached.Credentials;

        var tokenUrl = new Uri(new Uri(apiBaseUrl.TrimEnd('/') + "/"), "oauth2/token");
        using var request = LoliaOAuthProtocol.CreateTokenRequest(
            tokenUrl,
            credentials.ClientId,
            credentials.ClientSecret,
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = credentials.RefreshToken
            });
        using var response = await HttpClientFactory.CreateClient("frp-provider").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return credentials;
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = Data(document.RootElement);
        var accessToken = String(root, "access_token");
        if (string.IsNullOrWhiteSpace(accessToken)) return credentials;
        var refreshed = credentials with { AccessToken = accessToken, RefreshToken = String(root, "refresh_token") ?? credentials.RefreshToken };
        var expiresIn = Integer(root, "expires_in") ?? 3600;
        _credentialCache[accountId] = new(refreshed, DateTimeOffset.UtcNow.AddSeconds(Math.Max(120, expiresIn)));
        await credentialStore.UpdateAsync(accountId, refreshed, cancellationToken);
        return refreshed;
    }

    private sealed record CachedCredentials(ProviderCredentials Credentials, DateTimeOffset ExpiresAt);
}
