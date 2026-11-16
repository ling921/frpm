using Frpm.Abstractions.Models;
using Frpm.Domain.Enums;
using Frpm.Infrastructure.Providers;
using Frpm.Infrastructure.Providers.MeFrp;
using System.Net;
using System.Text.Json;

namespace Frpm.Infrastructure.Tests;

public sealed class MeFrpLaunchTests
{
    [Fact]
    public async Task PrepareLaunch_WhenMeFrpc_UsesQuickStartFlagsWithFrpToken()
    {
        HttpRequestMessage? captured = null;
        var provider = new MeFrpProvider(new FakeHttpClientFactory(request =>
        {
            captured = request;
            return Json("""{"code":200,"data":{"token":"frp-token-12345"},"message":"ok"}""");
        }));

        var tunnel = new ProviderTunnel("197510", "home2", null, "http", "332", "HK", "npm", 80, "csors.com", "inactive", "{}");
        var workingDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var spec = await provider.PrepareLaunchAsync(
                "https://api.mefrp.com/api",
                new ProviderCredentials("api-access-token"),
                tunnel,
                "/data/cli/mefrpc",
                workingDir,
                CancellationToken.None);

            Assert.Equal(["-t", "frp-token-12345", "-p", "197510", "-n"], spec.Arguments);
            Assert.Contains("frp-token-12345", spec.SensitiveValues);
            Assert.Contains("api-access-token", spec.SensitiveValues);
            Assert.Equal("https://api.mefrp.com/api/auth/user/frpToken", captured?.RequestUri?.ToString());
        }
        finally
        {
            if (Directory.Exists(workingDir)) Directory.Delete(workingDir, true);
        }
    }

    [Fact]
    public async Task PrepareLaunch_WhenFrpc_UsesConfigWithFixedMetadatas()
    {
        var rawToml = "[proxies.metadatas]\nmefrpRedirectHTTPS = true\n\n[proxies.transport]\nuseEncryption = false\n";
        var provider = new MeFrpProvider(new FakeHttpClientFactory(request =>
            Json(JsonSerializer.Serialize(new
            {
                code = 200,
                data = new { config = rawToml, type = "TOML" },
                message = "ok"
            }))));

        var tunnel = new ProviderTunnel("197510", "home2", null, "http", "332", "HK", "npm", 80, "csors.com", "inactive", "{}");
        var workingDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var spec = await provider.PrepareLaunchAsync(
                "https://api.mefrp.com/api",
                new ProviderCredentials("api-access-token"),
                tunnel,
                "frpc.exe",
                workingDir,
                CancellationToken.None);

            Assert.Equal(["-c", Path.Combine(workingDir, "frpc.toml")], spec.Arguments);
            var savedToml = await File.ReadAllTextAsync(Path.Combine(workingDir, "frpc.toml"));
            Assert.Contains("mefrpRedirectHTTPS = \"true\"", savedToml);
            Assert.Contains("useEncryption = false", savedToml);
        }
        finally
        {
            if (Directory.Exists(workingDir)) Directory.Delete(workingDir, true);
        }
    }

    [Fact]
    public void FixTomlConfig_QuotesBooleansAndNumbersOnlyInMetadatas()
    {
        var input = """
serverAddr = '1.2.3.4'
serverPort = 6666

[[proxies]]
name = 'test'
type = 'http'

[proxies.metadatas]
mefrpRedirectHTTPS = true
customFlag = false
nodeNum = 123
alreadyQuoted = "true"

[proxies.transport]
useEncryption = false
useCompression = true
""";

        var fixedToml = MeFrpProvider.FixTomlConfig(input);

        Assert.Contains("mefrpRedirectHTTPS = \"true\"", fixedToml);
        Assert.Contains("customFlag = \"false\"", fixedToml);
        Assert.Contains("nodeNum = \"123\"", fixedToml);
        Assert.Contains("alreadyQuoted = \"true\"", fixedToml);
        Assert.Contains("useEncryption = false", fixedToml);
        Assert.Contains("useCompression = true", fixedToml);
    }

    [Fact]
    public async Task KickTunnelAsync_PostsToKickEndpoint()
    {
        HttpRequestMessage? captured = null;
        var provider = new MeFrpProvider(new FakeHttpClientFactory(request =>
        {
            captured = request;
            return Json("""{"code":200,"data":null,"message":"ok"}""");
        }));

        await provider.KickTunnelAsync(
            "https://api.mefrp.com/api",
            new ProviderCredentials("api-access-token"),
            "197510",
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("https://api.mefrp.com/api/auth/proxy/kick", captured.RequestUri?.ToString());
        Assert.Equal(HttpMethod.Post, captured.Method);
        var body = await captured.Content!.ReadAsStringAsync();
        Assert.Contains("\"proxyId\":197510", body);
    }

    [Fact]
    public async Task SetTunnelDisabledAsync_PostsToToggleEndpoint()
    {
        HttpRequestMessage? captured = null;
        var provider = new MeFrpProvider(new FakeHttpClientFactory(request =>
        {
            captured = request;
            return Json("""{"code":200,"data":null,"message":"ok"}""");
        }));

        await provider.SetTunnelDisabledAsync(
            "https://api.mefrp.com/api",
            new ProviderCredentials("api-access-token"),
            "197510",
            isDisabled: false,
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("https://api.mefrp.com/api/auth/proxy/toggle", captured.RequestUri?.ToString());
        Assert.Equal(HttpMethod.Post, captured.Method);
        var body = await captured.Content!.ReadAsStringAsync();
        Assert.Contains("\"proxyId\":197510", body);
        Assert.Contains("\"isDisabled\":false", body);
    }

    [Fact]
    public async Task UpdateTunnelAsync_IncludesProxyIdInBody()
    {
        HttpRequestMessage? captured = null;
        var provider = new MeFrpProvider(new FakeHttpClientFactory(request =>
        {
            captured = request;
            return Json("""{"code":200,"data":null,"message":"ok"}""");
        }));

        var tunnel = new ProviderTunnel("197510", "home2", "remark", "http", "332", "HK", "127.0.0.1", 8080, "csors.com", "inactive", "{}");
        var requestObj = new SaveTunnelRequest
        {
            Name = "home2-renamed",
            Type = "http",
            NodeId = "332",
            LocalAddress = "127.0.0.1",
            LocalPort = 8080,
            CustomDomain = "csors.com",
            Remark = "remark"
        };
        await provider.UpdateTunnelAsync(
            "https://api.mefrp.com/api",
            new ProviderCredentials("api-access-token"),
            tunnel,
            requestObj,
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("https://api.mefrp.com/api/auth/proxy/update", captured.RequestUri?.ToString());
        Assert.Equal(HttpMethod.Post, captured.Method);
        var body = await captured.Content!.ReadAsStringAsync();
        Assert.Contains("\"proxyId\":197510", body);
        Assert.Contains("\"proxyName\":\"home2-renamed\"", body);
    }

    private static HttpResponseMessage Json(string content) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
        };

    private sealed class FakeHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FakeHandler(handler));

        private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(handler(request));
        }
    }
}
