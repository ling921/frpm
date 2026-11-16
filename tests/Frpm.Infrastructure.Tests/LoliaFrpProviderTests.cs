using Frpm.Abstractions.Models;
using Frpm.Domain.Enums;
using Frpm.Infrastructure.Providers;
using System.Net;
using System.Text.Json;

namespace Frpm.Infrastructure.Tests;

public sealed class LoliaFrpProviderTests
{
    [Fact]
    public async Task DeleteTunnelAsync_SendsDeleteRequestWithIdentifier()
    {
        var handler = new RecordingHttpMessageHandler(HttpStatusCode.OK, "{\"code\":200,\"msg\":\"删除成功\",\"data\":{\"message\":\"隧道已删除\"}}");
        var clientFactory = new SingleHttpClientFactory(handler);
        var provider = new LoliaFrpProvider(clientFactory, new FakeCredentialStore());

        var tunnel = new ProviderTunnel(
            RemoteId: "37730",
            Name: "d9f2cce477054fad80136e92d720733a",
            Remark: "test-tunnel",
            Type: "tcp",
            NodeId: "1",
            NodeName: "Node1",
            LocalAddress: "127.0.0.1",
            LocalPort: 8080,
            RemoteAddress: "10001",
            ProviderStatus: "active",
            PayloadJson: "{\"id\":37730,\"name\":\"d9f2cce477054fad80136e92d720733a\"}");

        var credentials = new ProviderCredentials("mock-access-token");
        await provider.DeleteTunnelAsync("https://api.lolia.link/api/v1/", credentials, tunnel, CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Delete, handler.LastRequest.Method);
        Assert.Equal("/api/v1/user/tunnel/d9f2cce477054fad80136e92d720733a", handler.LastRequest.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task DeleteTunnelAsync_WhenRemoteReturns404_IsIdempotentAndDoesNotThrow()
    {
        var handler = new RecordingHttpMessageHandler(HttpStatusCode.NotFound, "{\"code\":404,\"msg\":\"隧道不存在\"}");
        var clientFactory = new SingleHttpClientFactory(handler);
        var provider = new LoliaFrpProvider(clientFactory, new FakeCredentialStore());

        var tunnel = new ProviderTunnel(
            RemoteId: "37700",
            Name: "81197bb901d442eabf113bc0bf8a78a3",
            Remark: "missing-tunnel",
            Type: "tcp",
            NodeId: null,
            NodeName: null,
            LocalAddress: "127.0.0.1",
            LocalPort: 8080,
            RemoteAddress: "10002",
            ProviderStatus: "inactive",
            PayloadJson: "{}");

        var credentials = new ProviderCredentials("mock-access-token");
        // Must not throw when remote returns 404
        await provider.DeleteTunnelAsync("https://api.lolia.link/api/v1/", credentials, tunnel, CancellationToken.None);
    }

    [Fact]
    public async Task DeleteTunnelAsync_WhenIdentifierInPayload_ResolvesAndSends()
    {
        var handler = new RecordingHttpMessageHandler(HttpStatusCode.OK, "{\"code\":200,\"msg\":\"删除成功\"}");
        var clientFactory = new SingleHttpClientFactory(handler);
        var provider = new LoliaFrpProvider(clientFactory, new FakeCredentialStore());

        var tunnel = new ProviderTunnel(
            RemoteId: "",
            Name: "",
            Remark: "tunnel-with-empty-name",
            Type: "tcp",
            NodeId: null,
            NodeName: null,
            LocalAddress: "127.0.0.1",
            LocalPort: 8080,
            RemoteAddress: "10003",
            ProviderStatus: "inactive",
            PayloadJson: "{\"name\":\"extracted_hex_id\"}");

        var credentials = new ProviderCredentials("mock-access-token");
        await provider.DeleteTunnelAsync("https://api.lolia.link/api/v1/", credentials, tunnel, CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("/api/v1/user/tunnel/extracted_hex_id", handler.LastRequest.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task DeleteTunnelAsync_WhenNoIdentifierAvailable_DoesNotSendRequest()
    {
        var handler = new RecordingHttpMessageHandler(HttpStatusCode.OK, "{}");
        var clientFactory = new SingleHttpClientFactory(handler);
        var provider = new LoliaFrpProvider(clientFactory, new FakeCredentialStore());

        var tunnel = new ProviderTunnel(
            RemoteId: "",
            Name: "",
            Remark: null,
            Type: "tcp",
            NodeId: null,
            NodeName: null,
            LocalAddress: null,
            LocalPort: null,
            RemoteAddress: null,
            ProviderStatus: null,
            PayloadJson: "{}");

        var credentials = new ProviderCredentials("mock-access-token");
        await provider.DeleteTunnelAsync("https://api.lolia.link/api/v1/", credentials, tunnel, CancellationToken.None);

        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task GetTunnelsAsync_MapsRemarkAsDisplayName()
    {
        var json = """
        {
            "code": 200,
            "data": {
                "list": [
                    {
                        "id": 37739,
                        "name": "96bc2b867b6f42438d4cdc7ad5d9689d",
                        "remark": "dst-master (TCP)",
                        "type": "tcp",
                        "node_id": 74,
                        "node_name": "Node-74",
                        "local_ip": "127.0.0.1",
                        "local_port": 10888,
                        "remote_port": 22413,
                        "status": "inactive"
                    },
                    {
                        "id": 37740,
                        "name": "c6bc2b867b6f42438d4cdc7ad5d9689e",
                        "remark": "",
                        "type": "udp",
                        "node_id": 74,
                        "node_name": "Node-74",
                        "local_ip": "127.0.0.1",
                        "local_port": 10888,
                        "remote_port": 22413,
                        "status": "inactive"
                    }
                ],
                "page": 1,
                "total_page": 1
            }
        }
        """;
        var handler = new RecordingHttpMessageHandler(HttpStatusCode.OK, json);
        var clientFactory = new SingleHttpClientFactory(handler);
        var provider = new LoliaFrpProvider(clientFactory, new FakeCredentialStore());

        var page = await provider.GetTunnelsAsync("https://api.lolia.link/api/v1/", new ProviderCredentials("token"), 1, CancellationToken.None);

        Assert.Equal(2, page.Items.Count);
        // Remark is present, display name should be the remark
        Assert.Equal("dst-master (TCP)", page.Items[0].Name);
        Assert.Equal("37739", page.Items[0].RemoteId);
        Assert.Equal("dst-master (TCP)", page.Items[0].Remark);

        // Remark is empty, display name falls back to hex name
        Assert.Equal("c6bc2b867b6f42438d4cdc7ad5d9689e", page.Items[1].Name);
        Assert.Equal("37740", page.Items[1].RemoteId);
    }

    [Fact]
    public async Task CreateTunnelsAsync_WhenTcpUdp_SendsPostToTcpUdpEndpointAndReturnsBothTunnels()
    {
        var responseJson = """
        {
            "code": 201,
            "msg": "创建隧道成功",
            "data": {
                "tunnels": [
                    {
                        "id": 37757,
                        "name": "831b2942c1964ac4884758969bed3b98",
                        "remark": "test-dual (TCP)",
                        "type": "tcp",
                        "node_id": 74,
                        "local_ip": "127.0.0.1",
                        "local_port": 8888,
                        "remote_port": 22413,
                        "status": "inactive"
                    },
                    {
                        "id": 37758,
                        "name": "e04f3ec06c4f401bb2132de08becf73e",
                        "remark": "test-dual (UDP)",
                        "type": "udp",
                        "node_id": 74,
                        "local_ip": "127.0.0.1",
                        "local_port": 8888,
                        "remote_port": 22413,
                        "status": "inactive"
                    }
                ]
            }
        }
        """;
        var handler = new RecordingHttpMessageHandler(HttpStatusCode.Created, responseJson);
        var clientFactory = new SingleHttpClientFactory(handler);
        var provider = new LoliaFrpProvider(clientFactory, new FakeCredentialStore());

        var request = new SaveTunnelRequest
        {
            ProviderAccountId = Guid.NewGuid(),
            NodeId = "74",
            Type = "tcp+udp",
            LocalAddress = "127.0.0.1",
            LocalPort = 8888,
            RemotePort = 0,
            Name = "test-dual",
            Remark = "test-dual"
        };

        var tunnels = await provider.CreateTunnelsAsync("https://api.lolia.link/api/v1/", new ProviderCredentials("token"), request, CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal("/api/v1/user/tunnel/tcp-udp", handler.LastRequest.RequestUri?.AbsolutePath);

        Assert.Equal(2, tunnels.Count);
        Assert.Equal("tcp", tunnels[0].Type);
        Assert.Equal("test-dual (TCP)", tunnels[0].Name);
        Assert.Equal("37757", tunnels[0].RemoteId);
        Assert.Equal("22413", tunnels[0].RemoteAddress);

        Assert.Equal("udp", tunnels[1].Type);
        Assert.Equal("test-dual (UDP)", tunnels[1].Name);
        Assert.Equal("37758", tunnels[1].RemoteId);
        Assert.Equal("22413", tunnels[1].RemoteAddress);
    }

    private sealed class RecordingHttpMessageHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class SingleHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private sealed class FakeCredentialStore : IProviderCredentialStore
    {
        public Task UpdateAsync(Guid accountId, ProviderCredentials credentials, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
