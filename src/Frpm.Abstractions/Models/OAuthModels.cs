namespace Frpm.Abstractions.Models;

public sealed class StartLoliaOAuthRequest
{
    public Guid? AccountId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = "https://api.lolia.link/api/v1";
    public string ClientId { get; set; } = string.Empty;
    public string? ClientSecret { get; set; }
    public string CallbackUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public sealed record OAuthStartModel(string AuthorizationUrl);
