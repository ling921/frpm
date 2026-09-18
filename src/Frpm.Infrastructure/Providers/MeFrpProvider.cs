using Frpm.Abstractions.Models;
using Frpm.Domain.Enums;
using System.Text.Json;

namespace Frpm.Infrastructure.Providers.MeFrp;

[SingletonService(typeof(IFrpProvider), ServiceKey = nameof(ProviderType.MeFrp))]
public sealed class MeFrpProvider(IHttpClientFactory httpClientFactory) : HttpProviderBase(httpClientFactory), IFrpProvider
{
    public ProviderType Type => ProviderType.MeFrp;
    public ProviderCapabilitiesModel Capabilities => BuiltInProviderCatalog.Get(Type).Capabilities;

    public async Task ValidateAccountAsync(string apiBaseUrl, ProviderCredentials credentials, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(apiBaseUrl, credentials, HttpMethod.Get, "auth/user/info", null, cancellationToken);
        EnsureSuccess(document.RootElement);
    }

    public async Task<IReadOnlyList<ProviderNodeModel>> GetNodesAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        CancellationToken cancellationToken)
    {
        using var document = await SendAsync(apiBaseUrl, credentials, HttpMethod.Get, "auth/node/list", null, cancellationToken);
        EnsureSuccess(document.RootElement);
        var data = Data(document.RootElement);
        if (data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var nodes = data.EnumerateArray().Select(node => new ProviderNodeModel(
            String(node, "nodeId") ?? "",
            String(node, "name") ?? "未命名节点",
            String(node, "hostname"),
            Boolean(node, "isOnline") == true && Boolean(node, "isDisabled") != true,
            String(node, "region"),
            String(node, "bandwidth"),
            String(node, "allowType"),
            String(node, "allowPort"),
            String(node, "description"))).ToList();
        var addresses = await GetNodeAddressesAsync(apiBaseUrl, credentials, cancellationToken);
        return nodes.Select(node => string.IsNullOrWhiteSpace(node.Address)
            && addresses.TryGetValue(node.Id, out var address)
                ? node with { Address = address }
                : node).ToList();
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

        using var document = await SendAsync(apiBaseUrl, credentials, HttpMethod.Get, "auth/proxy/list", null, cancellationToken);
        EnsureSuccess(document.RootElement);
        var data = Data(document.RootElement);
        var proxies = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("proxies", out var list) ? list : default;
        var items = proxies.ValueKind == JsonValueKind.Array
            ? proxies.EnumerateArray().Select(MapTunnel).Where(tunnel => tunnel is not null).Cast<ProviderTunnel>().ToList()
            : [];
        return new(items, 1, 1);
    }

    public async Task<ProviderTunnel> CreateTunnelAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        SaveTunnelRequest request,
        CancellationToken cancellationToken)
    {
        using (var document = await SendAsync(
                   apiBaseUrl,
                   credentials,
                   HttpMethod.Post,
                   "auth/proxy/create",
                   CreateBody(request, request.Name, null),
                   cancellationToken))
        {
            EnsureSuccess(document.RootElement);
        }

        var page = await GetTunnelsAsync(apiBaseUrl, credentials, 1, cancellationToken);
        return page.Items.FirstOrDefault(tunnel => string.Equals(tunnel.Name, request.Name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("ME Frp 已接受创建请求，但未能在隧道列表中找到新隧道。");
    }

    public async Task<ProviderTunnel> UpdateTunnelAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        ProviderTunnel current,
        SaveTunnelRequest request,
        CancellationToken cancellationToken)
    {
        using var document = await SendAsync(
            apiBaseUrl,
            credentials,
            HttpMethod.Post,
            "auth/proxy/update",
            CreateBody(request, current.Name, current.NodeId),
            cancellationToken);
        EnsureSuccess(document.RootElement);
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
        var proxyId = ParseProxyId(tunnel.RemoteId);
        using var document = await SendAsync(
            apiBaseUrl,
            credentials,
            HttpMethod.Post,
            "auth/proxy/delete",
            new { proxyId },
            cancellationToken);
        EnsureSuccess(document.RootElement);
    }

    public async Task<LaunchSpecification> PrepareLaunchAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        ProviderTunnel tunnel,
        string executablePath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var proxyId = ParseProxyId(tunnel.RemoteId);
        using var document = await SendAsync(
            apiBaseUrl,
            credentials,
            HttpMethod.Post,
            "auth/proxy/config",
            new { proxyId, format = "toml" },
            cancellationToken);
        EnsureSuccess(document.RootElement);
        var config = String(Data(document.RootElement), "config")
            ?? throw new InvalidOperationException("ME Frp 未返回 frpc 配置。");
        Directory.CreateDirectory(workingDirectory);
        var configPath = Path.Combine(workingDirectory, "frpc.toml");
        await File.WriteAllTextAsync(configPath, config, cancellationToken);
        return new(executablePath, workingDirectory, ["-c", configPath], new Dictionary<string, string?>(), [credentials.AccessToken ?? ""]);
    }

    private static object CreateBody(SaveTunnelRequest request, string name, string? fallbackNodeId) => new
    {
        nodeId = int.TryParse(request.NodeId ?? fallbackNodeId, out var nodeId) ? nodeId : 0,
        proxyName = name,
        localIp = request.LocalAddress,
        localPort = request.LocalPort,
        remotePort = request.RemotePort,
        domain = string.IsNullOrWhiteSpace(request.CustomDomain)
            ? ""
            : JsonSerializer.Serialize(TunnelInputRules.ParseDomains(request.CustomDomain)),
        proxyType = request.Type,
        accessKey = string.Empty,
        hostHeaderRewrite = Advanced(request, "hostHeaderRewrite") ?? "",
        headerXFromWhere = Advanced(request, "headerXFromWhere") ?? "",
        proxyProtocolVersion = Advanced(request, "proxyProtocolVersion") ?? "",
        useEncryption = AdvancedBoolean(request, "useEncryption"),
        useCompression = AdvancedBoolean(request, "useCompression")
    };

    private static ProviderTunnel? MapTunnel(JsonElement value)
    {
        var id = String(value, "proxyId");
        var name = String(value, "proxyName");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var online = Boolean(value, "isOnline");
        return new(
            id,
            name,
            null,
            String(value, "proxyType") ?? "tcp",
            String(value, "nodeId"),
            null,
            String(value, "localIp"),
            Integer(value, "localPort"),
            ReadRemoteAddress(value),
            online is null ? null : online.Value ? "active" : "inactive",
            value.GetRawText());
    }

    private static ProviderTunnel FromRequest(SaveTunnelRequest request, string id, string name, string payload) =>
        new(
            id,
            name,
            null,
            request.Type,
            request.NodeId,
            null,
            request.LocalAddress,
            request.LocalPort,
            TunnelInputRules.NormalizeDomains(request.CustomDomain) ?? request.RemotePort?.ToString(),
            "inactive",
            payload);

    private async Task<IReadOnlyDictionary<string, string>> GetNodeAddressesAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = await SendAsync(apiBaseUrl, credentials, HttpMethod.Get, "auth/node/nameList", null, cancellationToken);
            EnsureSuccess(document.RootElement);
            var data = Data(document.RootElement);
            if (data.ValueKind != JsonValueKind.Array)
            {
                return new Dictionary<string, string>();
            }

            return data.EnumerateArray()
                .Select(node => (Id: String(node, "nodeId"), Address: String(node, "hostname")))
                .Where(node => !string.IsNullOrWhiteSpace(node.Id) && !string.IsNullOrWhiteSpace(node.Address))
                .ToDictionary(node => node.Id!, node => node.Address!.Trim(), StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // This endpoint only exposes addresses for nodes that already have tunnels. A failure must not hide nodes.
            return new Dictionary<string, string>();
        }
    }

    private static string? ReadRemoteAddress(JsonElement value)
    {
        var domain = String(value, "domain");
        if (!string.IsNullOrWhiteSpace(domain))
        {
            try
            {
                using var document = JsonDocument.Parse(domain);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var domains = document.RootElement.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString())
                        .Where(item => !string.IsNullOrWhiteSpace(item));
                    var joined = string.Join(", ", domains!);
                    if (!string.IsNullOrWhiteSpace(joined))
                    {
                        return joined;
                    }
                }
            }
            catch (JsonException)
            {
                return domain;
            }
        }

        return String(value, "remotePort");
    }

    private static string? Advanced(SaveTunnelRequest request, string key) =>
        request.AdvancedOptions.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    private static bool AdvancedBoolean(SaveTunnelRequest request, string key) =>
        bool.TryParse(Advanced(request, key), out var value) && value;

    private static int ParseProxyId(string value) =>
        int.TryParse(value, out var proxyId)
            ? proxyId
            : throw new InvalidOperationException("ME Frp 隧道 ID 格式无效。");

    private static void EnsureSuccess(JsonElement root)
    {
        var code = Integer(root, "code");
        if (code is null or 200)
        {
            return;
        }

        throw new InvalidOperationException($"ME Frp API 返回 {code}: {Sanitize(String(root, "message") ?? "未知错误")}");
    }
}
