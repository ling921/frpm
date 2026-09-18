namespace Frpm.Abstractions.Services;

/// <summary>
/// 读取运行环境并管理可动态生效的系统设置。
/// </summary>
[RemoteService("/api/system-settings")]
[RemoteAuthorize]
public interface ISystemSettingsService
{
    /// <summary>
    /// 获取当前系统设置、运行环境和同步调度状态。
    /// </summary>
    [Get]
    Task<SystemSettingsModel> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存系统设置并通知后台任务立即应用。
    /// </summary>
    [Put]
    Task<SystemSettingsModel> SaveAsync(SaveSystemSettingsRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 请求后台任务尽快执行一次供应商同步。
    /// </summary>
    [Post("sync-now")]
    Task RequestProviderSyncAsync(CancellationToken cancellationToken = default);
}
