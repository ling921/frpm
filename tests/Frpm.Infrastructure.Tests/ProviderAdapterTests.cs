using Frpm.Infrastructure.Providers;
using Frpm.Infrastructure.Providers.MeFrp;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Frpm.Infrastructure.Tests;

public sealed class ProviderAdapterTests
{
    [Fact]
    public async Task Sakura_maps_tunnel_array_into_common_model()
    {
        var provider = new SakuraFrpProvider(new FakeHttpClientFactory(request => Json("""
            [{"id":114514,"name":"ssh","type":"tcp","node":5,"node_name":"HK","local_ip":"127.0.0.1","local_port":22,"remote":6000,"domain":"ssh.example.test","status":0,"online":true}]
            """)));

        var page = await provider.GetTunnelsAsync("https://example.test/v4", new ProviderCredentials("secret"), 1, CancellationToken.None);

        var tunnel = Assert.Single(page.Items);
        Assert.Equal("114514", tunnel.RemoteId);
        Assert.Equal("ssh", tunnel.Name);
        Assert.Equal(22, tunnel.LocalPort);
        Assert.Equal("6000", tunnel.RemoteAddress);
        Assert.Contains("ssh.example.test", tunnel.PayloadJson);
        Assert.Equal("active", tunnel.ProviderStatus);
    }

    [Fact]
    public async Task Sakura_maps_offline_and_unavailable_online_state_separately()
    {
        var provider = new SakuraFrpProvider(new FakeHttpClientFactory(_ => Json("""
            [
              {"id":1,"name":"offline","type":"tcp","status":0,"online":false},
              {"id":2,"name":"unavailable","type":"tcp","status":0}
            ]
            """)));

        var page = await provider.GetTunnelsAsync("https://example.test/v4", new ProviderCredentials("secret"), 1, CancellationToken.None);

        Assert.Equal("inactive", page.Items[0].ProviderStatus);
        Assert.Null(page.Items[1].ProviderStatus);
    }

    [Fact]
    public async Task Sakura_maps_node_dictionary_keys_and_flag_capabilities()
    {
        var provider = new SakuraFrpProvider(new FakeHttpClientFactory(_ => Json("""
            {
              "5": {"name":"香港 BGP","host":"hk.example.test","description":"三线节点","vip":0,"flag":39},
              "8": {"name":"离线节点","host":"offline.example.test","description":"维护中","vip":0,"flag":516}
            }
            """)));

        var nodes = await provider.GetNodesAsync("https://example.test/v4", new ProviderCredentials("secret"), CancellationToken.None);

        Assert.Equal("5", nodes[0].Id);
        Assert.Equal("香港 BGP", nodes[0].Name);
        Assert.True(nodes[0].Available);
        Assert.Equal("TCP / UDP / HTTP / HTTPS", nodes[0].AllowedTypes);
        Assert.Equal("三线节点", nodes[0].Description);
        Assert.False(nodes[1].Available);
    }

    [Fact]
    public async Task Lolia_honors_pagination_and_maps_tunnel_name()
    {
        var provider = new LoliaFrpProvider(new FakeHttpClientFactory(request => Json("""
            {"code":200,"data":{"page":1,"total_page":3,"list":[{"id":20,"name":"abc123","remark":"web","type":"http","node_id":1,"node_name":"Test","node_address":"edge.example.test","local_ip":"127.0.0.1","local_port":8080,"custom_domain":"demo.example.com","status":"inactive"}]}}
            """)), new FakeCredentialStore());

        var page = await provider.GetTunnelsAsync("https://example.test/api/v1", new ProviderCredentials("secret"), 1, CancellationToken.None);

        Assert.Equal(3, page.TotalPages);
        var tunnel = Assert.Single(page.Items);
        Assert.Equal("20", tunnel.RemoteId);
        Assert.Equal("abc123", tunnel.Name);
        Assert.Equal("demo.example.com", tunnel.RemoteAddress);
        Assert.Contains("edge.example.test", tunnel.PayloadJson);
    }

    [Fact]
    public async Task Lolia_maps_available_node_metadata()
    {
        var provider = new LoliaFrpProvider(new FakeHttpClientFactory(_ => Json("""
            {"code":200,"data":{"nodes":[{
              "id":3,"name":"香港节点","status":"online","ip_address":"hk.example.test",
              "supported_protocols":["tcp","udp","http","https"],"bandwidth":200
            }]}}
            """)), new FakeCredentialStore());

        var node = Assert.Single(await provider.GetNodesAsync("https://example.test/api/v1", new ProviderCredentials("secret"), CancellationToken.None));

        Assert.Equal("hk.example.test", node.Address);
        Assert.Equal("TCP / UDP / HTTP / HTTPS", node.AllowedTypes?.ToUpperInvariant());
        Assert.Equal("200 Mbps", node.Bandwidth);
    }

    [Fact]
    public async Task Lolia_launches_current_tunnel_with_detail_token()
    {
        HttpRequestMessage? captured = null;
        var provider = new LoliaFrpProvider(new FakeHttpClientFactory(request =>
        {
            captured = request;
            return Json("""{"code":200,"data":{"id":17900,"name":"wn0aftmw3xi2ergs81yic1yxrmn8103w","tunnel_token":"secret-token"}}""");
        }), new FakeCredentialStore());
        var tunnel = new ProviderTunnel(
            "17900", "wn0aftmw3xi2ergs81yic1yxrmn8103w", null, "tcp", "1", "Test", "127.0.0.1", 8080, "5000", "inactive", "{}");

        var specification = await provider.PrepareLaunchAsync(
            "https://example.test/api/v1",
            new ProviderCredentials("oauth-access-token"),
            tunnel,
            "frpc.exe",
            "run-directory",
            CancellationToken.None);

        Assert.Equal(["-t", "17900:secret-token"], specification.Arguments);
        Assert.Contains("17900:secret-token", specification.SensitiveValues);
        Assert.Contains("secret-token", specification.SensitiveValues);
        Assert.Equal("1", specification.Environment["CLICOLOR_FORCE"]);
        Assert.Equal("xterm-256color", specification.Environment["TERM"]);
        Assert.Equal("/api/v1/user/tunnel/wn0aftmw3xi2ergs81yic1yxrmn8103w", captured?.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task MeFrp_uses_bearer_auth_and_maps_documented_proxy_shape()
    {
        HttpRequestMessage? captured = null;
        var provider = new MeFrpProvider(new FakeHttpClientFactory(request =>
        {
            captured = request;
            return Json("""
                {
                  "code": 200,
                  "data": {
                    "nodes": [{"nodeId": 7, "name": "HK", "hostname": "hk.example.test", "allowGroup": "default"}],
                    "proxies": [{
                      "proxyId": 42,
                      "proxyName": "web",
                      "proxyType": "http",
                      "isBanned": false,
                      "isDisabled": false,
                      "localIp": "127.0.0.1",
                      "localPort": 8080,
                      "remotePort": 0,
                      "nodeId": 7,
                      "isOnline": true,
                      "domain": "[\"demo.example.com\"]"
                    }]
                  },
                  "message": "获取隧道列表成功"
                }
                """);
        }));

        var page = await provider.GetTunnelsAsync("https://api.mefrp.com/api", new ProviderCredentials("me-token"), 1, CancellationToken.None);

        var tunnel = Assert.Single(page.Items);
        Assert.Equal("42", tunnel.RemoteId);
        Assert.Equal("web", tunnel.Name);
        Assert.Equal("demo.example.com", tunnel.RemoteAddress);
        Assert.Equal("active", tunnel.ProviderStatus);
        Assert.NotNull(captured);
        Assert.Equal("https://api.mefrp.com/api/auth/proxy/list", captured.RequestUri?.ToString());
        Assert.Equal("Bearer", captured.Headers.Authorization?.Scheme);
        Assert.Equal("me-token", captured.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task MeFrp_maps_node_availability_and_api_errors()
    {
        var provider = new MeFrpProvider(new FakeHttpClientFactory(request =>
            request.RequestUri?.AbsolutePath.EndsWith("/auth/node/list", StringComparison.Ordinal) == true
                ? Json("""
                    {"code":200,"data":[
                      {"nodeId":1,"name":"可用","hostname":"one.example.test","region":"cn","bandwidth":"200Mbps","allowType":"tcp;udp","allowPort":"40000-65535","isOnline":true,"isDisabled":false},
                      {"nodeId":2,"name":"已禁用","hostname":"two.example.test","isOnline":true,"isDisabled":true}
                    ],"message":"ok"}
                    """)
                : Json("""{"code":401,"data":null,"message":"Token 无效"}""")));

        var nodes = await provider.GetNodesAsync("https://api.mefrp.com/api", new ProviderCredentials("secret"), CancellationToken.None);
        Assert.True(nodes[0].Available);
        Assert.Equal("cn", nodes[0].Region);
        Assert.Equal("200Mbps", nodes[0].Bandwidth);
        Assert.Equal("tcp;udp", nodes[0].AllowedTypes);
        Assert.Equal("40000-65535", nodes[0].PortRange);
        Assert.False(nodes[1].Available);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ValidateAccountAsync("https://api.mefrp.com/api", new ProviderCredentials("secret"), CancellationToken.None));
        Assert.Contains("Token 无效", exception.Message);
    }

    [Fact]
    public async Task MeFrp_uses_name_list_address_for_existing_tunnel_node()
    {
        var provider = new MeFrpProvider(new FakeHttpClientFactory(request =>
            request.RequestUri?.AbsolutePath.EndsWith("/auth/node/nameList", StringComparison.Ordinal) == true
                ? Json("""{"code":200,"data":[{"nodeId":7,"name":"HK","hostname":"hk-node.mefrp.com"}],"message":"ok"}""")
                : Json("""{"code":200,"data":[{"nodeId":7,"name":"HK","hostname":"","isOnline":true,"isDisabled":false}],"message":"ok"}""")));

        var node = Assert.Single(await provider.GetNodesAsync("https://api.mefrp.com/api", new ProviderCredentials("secret"), CancellationToken.None));

        Assert.Equal("hk-node.mefrp.com", node.Address);
    }

    [Fact]
    public async Task MeFrp_create_uses_documented_body_and_refreshes_remote_id()
    {
        string? createBody = null;
        var provider = new MeFrpProvider(new FakeHttpClientFactory(request =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/auth/proxy/create", StringComparison.Ordinal) == true)
            {
                createBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return Json("""{"code":200,"data":null,"message":"创建隧道成功"}""");
            }

            return Json("""
                {"code":200,"data":{"nodes":[],"proxies":[{
                  "proxyId":88,"proxyName":"site","proxyType":"http","localIp":"127.0.0.1",
                  "localPort":8080,"remotePort":0,"nodeId":3,"isOnline":false,
                  "domain":"[\"site.example.com\"]"
                }]},"message":"ok"}
                """);
        }));

        var tunnel = await provider.CreateTunnelAsync(
            "https://api.mefrp.com/api",
            new ProviderCredentials("secret"),
            new()
            {
                Name = "site",
                Type = "http",
                NodeId = "3",
                LocalAddress = "127.0.0.1",
                LocalPort = 8080,
                CustomDomain = "site.example.com"
            },
            CancellationToken.None);

        Assert.Equal("88", tunnel.RemoteId);
        using var body = JsonDocument.Parse(Assert.IsType<string>(createBody));
        Assert.Equal(3, body.RootElement.GetProperty("nodeId").GetInt32());
        Assert.Equal("[\"site.example.com\"]", body.RootElement.GetProperty("domain").GetString());
        Assert.Equal("http", body.RootElement.GetProperty("proxyType").GetString());
    }

    [Fact]
    public async Task Sakura_update_preserves_current_node_when_editor_does_not_change_it()
    {
        var provider = new SakuraFrpProvider(new FakeHttpClientFactory(_ => Json("{}")));
        var current = new ProviderTunnel("1", "tcp-one", null, "tcp", "5", "香港节点", "127.0.0.1", 80, "5000", "inactive", "{}");

        var updated = await provider.UpdateTunnelAsync(
            "https://example.test/v4",
            new ProviderCredentials("secret"),
            current,
            new() { Name = current.Name, Type = current.Type, LocalAddress = "127.0.0.1", LocalPort = 8080 },
            CancellationToken.None);

        Assert.Equal("5", updated.NodeId);
        Assert.Equal("香港节点", updated.NodeName);
    }

    [Fact]
    public async Task Lolia_update_preserves_current_node_when_editor_does_not_change_it()
    {
        var provider = new LoliaFrpProvider(new FakeHttpClientFactory(_ => Json("""{"code":200,"data":{}}""")), new FakeCredentialStore());
        var current = new ProviderTunnel("20", "tcp-two", null, "tcp", "7", "东京节点", "127.0.0.1", 80, "5000", "inactive", "{}");

        var updated = await provider.UpdateTunnelAsync(
            "https://example.test/api/v1",
            new ProviderCredentials("secret"),
            current,
            new() { Name = current.Name, Type = current.Type, LocalAddress = "127.0.0.1", LocalPort = 8080 },
            CancellationToken.None);

        Assert.Equal("7", updated.NodeId);
        Assert.Equal("东京节点", updated.NodeName);
    }

    [Fact]
    public async Task Provider_rejects_missing_access_token_before_http_call()
    {
        var provider = new SakuraFrpProvider(new FakeHttpClientFactory(_ => throw new InvalidOperationException("HTTP should not be called")));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ValidateAccountAsync("https://example.test", new ProviderCredentials(null), CancellationToken.None));
        Assert.Contains("访问令牌", exception.Message);
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };

    private sealed class FakeHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responder) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(responder));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responder(request));
    }

    private sealed class FakeCredentialStore : IProviderCredentialStore
    {
        public Task UpdateAsync(Guid accountId, ProviderCredentials credentials, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
