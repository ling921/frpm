namespace Frpm.Abstractions.Services;

/// <summary>
/// 管理远端隧道配置、本地进程和运行历史。
/// </summary>
[RemoteService("/api/tunnels")]
[RemoteAuthorize]
public interface ITunnelService
{
    /// <summary>
    /// 获取仪表盘使用的隧道统计。
    /// </summary>
    [Get("dashboard")]
    Task<DashboardModel> GetDashboardAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取本地已知的隧道列表。
    /// </summary>
    [Get]
    Task<IReadOnlyList<TunnelModel>> GetListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取 FRPM 所在机器可用于绑定的本地地址建议。
    /// </summary>
    [Get("local-addresses")]
    Task<IReadOnlyList<string>> GetLocalAddressSuggestionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建或更新远端隧道并保存其本地映射。
    /// </summary>
    [Post]
    Task<TunnelModel> SaveAsync(SaveTunnelRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止并删除指定远端隧道。
    /// </summary>
    [Delete("{id}")]
    Task DeleteAsync([Path] Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 启动指定隧道的本地 CLI 进程。
    /// </summary>
    [Post("{id}/start")]
    Task StartAsync([Path] Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止指定隧道的本地 CLI 进程。
    /// </summary>
    [Post("{id}/stop")]
    Task StopAsync([Path] Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 从 FRPM 所在机器测试隧道的本地服务端点。
    /// </summary>
    [Post("test-local-endpoint")]
    Task<LocalEndpointTestResult> TestLocalEndpointAsync(LocalEndpointTestRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取隧道运行历史，可按隧道筛选。
    /// </summary>
    [Get("runs")]
    Task<IReadOnlyList<TunnelRunModel>> GetRunsAsync([Query] Guid? tunnelId = null, CancellationToken cancellationToken = default);
}
