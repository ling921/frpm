using Frpm.Abstractions.Models;
using Frpm.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace Frpm.Infrastructure.Tests;

public sealed class LoliaOAuthProtocolTests
{
    [Fact]
    public async Task Authorization_url_contains_lolia_scope_offline_access_and_exact_callback()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new ProviderOAuthService(null!, null!, configuration);

        var result = await service.StartLoliaAsync(new StartLoliaOAuthRequest
        {
            DisplayName = "test",
            ClientId = "client-id",
            CallbackUrl = "https://localhost:7114/oauth/lolia/callback"
        });

        var query = ParseQuery(new Uri(result.AuthorizationUrl).Query);
        Assert.Equal("all", query["scope"]);
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("https://localhost:7114/oauth/lolia/callback", query["redirect_uri"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrWhiteSpace(query["state"]));
    }

    [Fact]
    public async Task Token_request_sends_client_credentials_in_form_instead_of_basic_auth()
    {
        using var request = LoliaOAuthProtocol.CreateTokenRequest(
            new Uri("https://api.lolia.link/api/v1/oauth2/token"),
            "client-id",
            "client-secret",
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = "code",
                ["code_verifier"] = "verifier"
            });

        Assert.Null(request.Headers.Authorization);
        var form = ParseQuery(await request.Content!.ReadAsStringAsync());
        Assert.Equal("client-id", form["client_id"]);
        Assert.Equal("client-secret", form["client_secret"]);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("verifier", form["code_verifier"]);
    }

    private static Dictionary<string, string> ParseQuery(string value) => value
        .TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .ToDictionary(
            part => Uri.UnescapeDataString(part[0].Replace('+', ' ')),
            part => Uri.UnescapeDataString((part.Length > 1 ? part[1] : "").Replace('+', ' ')));
}
