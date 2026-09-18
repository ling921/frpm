namespace Frpm.Abstractions.Services;

/// <summary>
/// 创建供应商 OAuth 授权流程。
/// </summary>
[RemoteService("/api/oauth")]
[RemoteAuthorize]
public interface IProviderOAuthService
{
    /// <summary>
    /// 创建 LoliaFrp 授权请求并返回授权地址。
    /// </summary>
    [Post("lolia/start")]
    Task<OAuthStartModel> StartLoliaAsync(StartLoliaOAuthRequest request, CancellationToken cancellationToken = default);
}
