using System.Net;
using System.Net.Sockets;
using Frpm.Abstractions.Models;
using Frpm.Abstractions.Services;
using Frpm.Application;

namespace Frpm.Desktop.Tests;

public sealed class DesktopOAuthTests
{
    [Fact]
    public async Task Callback_checks_state_and_closes_listener_after_authorization()
    {
        var port = FreePort();
        var oauth = new FakeOAuth();
        var flow = new DesktopOAuthFlow(oauth, oauth);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        Task? browser = null;
        await flow.AuthorizeLoliaAsync(Request(port), _ =>
        {
            browser = Task.Run(async () =>
            {
                using var wrong = await client.GetAsync($"http://127.0.0.1:{port}/oauth/lolia/callback?code=wrong&state=wrong");
                Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
                using var right = await client.GetAsync($"http://127.0.0.1:{port}/oauth/lolia/callback?code=valid&state=expected");
                Assert.Equal(HttpStatusCode.OK, right.StatusCode);
            });
            return Task.CompletedTask;
        });
        await browser!;
        Assert.Equal("valid", oauth.CompletedCode);
        using var next = new TcpListener(IPAddress.Loopback, port);
        next.Start();
    }

    [Fact]
    public async Task Cancelling_authorization_removes_pending_state_and_releases_port()
    {
        var port = FreePort();
        var oauth = new FakeOAuth();
        var flow = new DesktopOAuthFlow(oauth, oauth);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flow.AuthorizeLoliaAsync(Request(port), _ => Task.CompletedTask, cancellation.Token));
        Assert.True(oauth.Cancelled);
        using var next = new TcpListener(IPAddress.Loopback, port);
        next.Start();
    }

    [Fact]
    public async Task Busy_callback_port_does_not_create_pending_authorization()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var oauth = new FakeOAuth();
        var flow = new DesktopOAuthFlow(oauth, oauth);
        await Assert.ThrowsAsync<InvalidOperationException>(() => flow.AuthorizeLoliaAsync(Request(((IPEndPoint)listener.LocalEndpoint).Port), _ => Task.CompletedTask));
        Assert.False(oauth.Started);
    }

    private static StartLoliaOAuthRequest Request(int port) => new() { DisplayName = "test", ClientId = "client", CallbackUrl = $"http://127.0.0.1:{port}/oauth/lolia/callback" };
    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class FakeOAuth : IProviderOAuthService, IProviderOAuthCallback
    {
        public bool Started { get; private set; }
        public bool Cancelled { get; private set; }
        public string? CompletedCode { get; private set; }
        public Task<OAuthStartModel> StartLoliaAsync(StartLoliaOAuthRequest request, CancellationToken ct = default)
        {
            Started = true;
            return Task.FromResult(new OAuthStartModel("https://example.test/authorize?state=expected"));
        }
        public Task CompleteLoliaAsync(string code, string state, CancellationToken ct = default)
        {
            Assert.Equal("expected", state);
            CompletedCode = code;
            return Task.CompletedTask;
        }
        public void CancelLolia(string state)
        {
            if (CompletedCode is not null) throw new InvalidOperationException();
            Cancelled = true;
        }
    }
}
