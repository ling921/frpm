using Frpm.Abstractions.Models;
using Frpm.Abstractions.Services;
using Frpm.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Frpm.Infrastructure.Services;

[SingletonService(typeof(IProviderOAuthService))]
public sealed class ProviderOAuthService(
    IHttpClientFactory httpClientFactory,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration) : IProviderOAuthService
{
    private readonly ConcurrentDictionary<string, PendingAuthorization> _pending = new();

    public Task<OAuthStartModel> StartLoliaAsync(StartLoliaOAuthRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.ClientId))
        {
            throw new InvalidOperationException("OAuth 账号名称和 Client ID 不能为空。");
        }

        if (!Uri.TryCreate(request.CallbackUrl, UriKind.Absolute, out var callback)
            || callback.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("OAuth 回调地址无效。");
        }

        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        _pending[state] = new(request, verifier, DateTimeOffset.UtcNow.AddMinutes(10));
        CleanupExpired();
        var authorizeUrl = configuration["Frpm:Providers:Lolia:AuthorizeUrl"] ?? "https://dash.lolia.link/oauth/authorize";
        var scope = configuration["Frpm:Providers:Lolia:Scope"] ?? "all";
        var query = string.Join('&',
            $"client_id={Uri.EscapeDataString(request.ClientId)}",
            "response_type=code",
            $"redirect_uri={Uri.EscapeDataString(request.CallbackUrl)}",
            $"scope={Uri.EscapeDataString(scope)}",
            $"state={Uri.EscapeDataString(state)}",
            $"code_challenge={Uri.EscapeDataString(challenge)}",
            "code_challenge_method=S256",
            "access_type=offline");
        return Task.FromResult(new OAuthStartModel($"{authorizeUrl}?{query}"));
    }

    public async Task CompleteLoliaAsync(string code, string state, CancellationToken cancellationToken)
    {
        if (!_pending.TryRemove(state, out var pending) || pending.ExpiresAt < DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("OAuth 状态已失效，请重新授权。");
        }

        var tokenUrl = new Uri(new Uri(pending.Request.ApiBaseUrl.TrimEnd('/') + "/"), "oauth2/token");
        using var request = LoliaOAuthProtocol.CreateTokenRequest(
            tokenUrl,
            pending.Request.ClientId,
            pending.Request.ClientSecret,
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = pending.Request.CallbackUrl,
                ["code_verifier"] = pending.Verifier
            });
        using var response = await httpClientFactory.CreateClient("frp-provider").SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var message = LoliaOAuthProtocol.SanitizeResponse(json);
            throw new InvalidOperationException(
                $"OAuth token 交换失败：{(int)response.StatusCode} {message}".TrimEnd());
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement.TryGetProperty("data", out var data) ? data : document.RootElement;
        var accessToken = root.TryGetProperty("access_token", out var access) ? access.GetString() : null;
        var refreshToken = root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("OAuth 响应中没有 access_token。");
        }

        using var scope = scopeFactory.CreateScope();
        var accountService = scope.ServiceProvider.GetRequiredService<IProviderAccountService>();
        await accountService.SaveAsync(new SaveProviderAccountRequest
        {
            Id = pending.Request.AccountId,
            ProviderType = ProviderType.LoliaFrp,
            DisplayName = pending.Request.DisplayName,
            ApiBaseUrl = pending.Request.ApiBaseUrl,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ClientId = pending.Request.ClientId,
            ClientSecret = pending.Request.ClientSecret,
            Enabled = pending.Request.Enabled
        }, cancellationToken);
    }

    public void CancelLolia(string state)
    {
        if (!_pending.TryRemove(state, out var pending) || pending.ExpiresAt < DateTimeOffset.UtcNow)
            throw new InvalidOperationException("OAuth 状态已失效，请重新授权。");
    }

    private void CleanupExpired()
    {
        foreach (var item in _pending.Where(item => item.Value.ExpiresAt < DateTimeOffset.UtcNow))
        {
            _pending.TryRemove(item.Key, out _);
        }
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
    private sealed record PendingAuthorization(StartLoliaOAuthRequest Request, string Verifier, DateTimeOffset ExpiresAt);
}
