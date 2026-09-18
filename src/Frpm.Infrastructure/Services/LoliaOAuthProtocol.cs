namespace Frpm.Infrastructure.Services;

internal static class LoliaOAuthProtocol
{
    public static HttpRequestMessage CreateTokenRequest(
        Uri tokenUrl,
        string clientId,
        string? clientSecret,
        IReadOnlyDictionary<string, string> grantParameters)
    {
        var parameters = new Dictionary<string, string>(grantParameters)
        {
            ["client_id"] = clientId
        };
        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            // LoliaFRP uses client_secret_post (the Go OAuth client's AuthStyleInParams).
            parameters["client_secret"] = clientSecret;
        }

        return new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(parameters)
        };
    }

    public static string SanitizeResponse(string value)
    {
        const int maxLength = 500;
        value = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return value.Length <= maxLength ? value : value[..maxLength] + "…";
    }
}
