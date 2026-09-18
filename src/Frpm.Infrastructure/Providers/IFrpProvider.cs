using Frpm.Abstractions.Models;
using Frpm.Domain.Enums;

namespace Frpm.Infrastructure.Providers;

/// <summary>
/// 调用供应商 API 所需的凭据。
/// </summary>
public sealed record ProviderCredentials(
    string? AccessToken,
    string? RefreshToken = null,
    string? ClientId = null,
    string? ClientSecret = null,
    Guid? AccountId = null);

/// <summary>
/// 负责在凭据刷新后安全地更新账号存储。
/// </summary>
public interface IProviderCredentialStore
{
    Task UpdateAsync(Guid accountId, ProviderCredentials credentials, CancellationToken cancellationToken);
}

/// <summary>
/// 供应商返回的标准化隧道数据。
/// </summary>
public sealed record ProviderTunnel(
    string RemoteId,
    string Name,
    string? Remark,
    string Type,
    string? NodeId,
    string? NodeName,
    string? LocalAddress,
    int? LocalPort,
    string? RemoteAddress,
    string? ProviderStatus,
    string PayloadJson);

/// <summary>
/// 供应商隧道的分页结果。
/// </summary>
public sealed record ProviderTunnelPage(IReadOnlyList<ProviderTunnel> Items, int Page, int TotalPages);

/// <summary>
/// 启动本地 CLI 进程所需的完整且可脱敏参数。
/// </summary>
public sealed record LaunchSpecification(
    string ExecutablePath,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string?> Environment,
    IReadOnlyList<string> SensitiveValues);

/// <summary>
/// 内置 FRP 供应商适配器的统一契约。
/// </summary>
public interface IFrpProvider
{
    /// <summary>
    /// 获取该适配器对应的供应商类型。
    /// </summary>
    ProviderType Type { get; }

    /// <summary>
    /// 获取供应商支持的管理能力。
    /// </summary>
    ProviderCapabilitiesModel Capabilities { get; }

    /// <summary>
    /// 验证账号凭据是否可用于访问供应商 API。
    /// </summary>
    Task ValidateAccountAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        CancellationToken cancellationToken);

    /// <summary>
    /// 获取供应商节点。
    /// </summary>
    Task<IReadOnlyList<ProviderNodeModel>> GetNodesAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        CancellationToken cancellationToken);

    /// <summary>
    /// 分页获取远端隧道。
    /// </summary>
    Task<ProviderTunnelPage> GetTunnelsAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        int page,
        CancellationToken cancellationToken);

    /// <summary>
    /// 创建远端隧道。
    /// </summary>
    Task<ProviderTunnel> CreateTunnelAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        SaveTunnelRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// 更新远端隧道。
    /// </summary>
    Task<ProviderTunnel> UpdateTunnelAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        ProviderTunnel current,
        SaveTunnelRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// 删除远端隧道。
    /// </summary>
    Task DeleteTunnelAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        ProviderTunnel tunnel,
        CancellationToken cancellationToken);

    /// <summary>
    /// 根据远端隧道详情准备本地 CLI 启动参数。
    /// </summary>
    Task<LaunchSpecification> PrepareLaunchAsync(
        string apiBaseUrl,
        ProviderCredentials credentials,
        ProviderTunnel tunnel,
        string executablePath,
        string workingDirectory,
        CancellationToken cancellationToken);
}

/// <summary>
/// 按照供应商类型解析内置适配器。
/// </summary>
public interface IFrpProviderFactory
{
    IFrpProvider Get(ProviderType type);
}
