using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Frpm.Infrastructure.Providers;

public abstract class HttpProviderBase(IHttpClientFactory httpClientFactory)
{
    protected IHttpClientFactory HttpClientFactory { get; } = httpClientFactory;
    protected async Task<JsonDocument> SendAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credentials.AccessToken))
        {
            throw new InvalidOperationException("访问令牌不能为空。");
        }

        var request = new HttpRequestMessage(method, new Uri(new Uri(apiBaseUrl.TrimEnd('/') + "/"), path.TrimStart('/')));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("Frpm/1.0");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await HttpClientFactory.CreateClient("frp-provider").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"第三方 API 返回 {(int)response.StatusCode}: {Sanitize(content)}");
        }

        return JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content);
    }

    protected static string? String(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }
        return null;
    }

    protected static int? Integer(JsonElement element, params string[] names)
    {
        var value = String(element, names);
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    protected static bool? Boolean(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return value.GetBoolean();
            }

            if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    protected static JsonElement Data(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data)
            ? data
            : root;

    protected static string Sanitize(string value)
    {
        const int maxLength = 500;
        value = value.Replace('\r', ' ').Replace('\n', ' ');
        return value.Length <= maxLength ? value : value[..maxLength] + "…";
    }
}
