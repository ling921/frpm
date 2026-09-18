using Frpm.Abstractions.Services;
using Frpm.Infrastructure.Services;
using System.Text.Json;

namespace Frpm.Api;

public static class FrpmApiEndpoints
{
    public static IEndpointRouteBuilder MapFrpmApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/oauth/lolia/callback", async (HttpContext context, string? code, string? state, string? error, IProviderOAuthService service, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(state))
                return OAuthPopupResult(context, false, "OAuth 回调缺少 state，请重新授权。");

            try
            {
                var oauthService = (ProviderOAuthService)service;
                if (!string.IsNullOrWhiteSpace(error))
                {
                    oauthService.CancelLolia(state);
                    return OAuthPopupResult(context, false, $"LoliaFrp 拒绝授权：{error}");
                }

                if (string.IsNullOrWhiteSpace(code))
                {
                    oauthService.CancelLolia(state);
                    return OAuthPopupResult(context, false, "OAuth 回调缺少授权码，请重新授权。");
                }

                await oauthService.CompleteLoliaAsync(code, state, ct);
                return OAuthPopupResult(context, true, "LoliaFrp OAuth 授权成功。");
            }
            catch (Exception ex)
            {
                return OAuthPopupResult(context, false, ex.Message);
            }
        }).AllowAnonymous();

        return endpoints;
    }

    private static IResult OAuthPopupResult(HttpContext context, bool success, string message)
    {
        var origin = $"{context.Request.Scheme}://{context.Request.Host}";
        var fallback = success
            ? "/providers?oauthSuccess=true"
            : $"/providers?oauthError={Uri.EscapeDataString(message)}";
        var payload = JsonSerializer.Serialize(new { type = "frpm:lolia-oauth", success, message });
        var originJson = JsonSerializer.Serialize(origin);
        var fallbackJson = JsonSerializer.Serialize(fallback);
        var html = $$"""
            <!doctype html>
            <html lang="zh-CN">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>FRPM OAuth</title></head>
            <body>
              <p>{{System.Net.WebUtility.HtmlEncode(message)}}</p>
              <script>
                (() => {
                  const payload = {{payload}};
                  if (window.opener && !window.opener.closed) {
                    window.opener.postMessage(payload, {{originJson}});
                    window.close();
                    return;
                  }
                  window.location.replace({{fallbackJson}});
                })();
              </script>
            </body>
            </html>
            """;
        return Results.Content(html, "text/html; charset=utf-8");
    }
}
