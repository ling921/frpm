namespace Frpm.Abstractions.Services;

/// <summary>
/// 管理各供应商在当前系统与架构下使用的 CLI 软件包。
/// </summary>
[RemoteService("/api/cli-packages")]
[RemoteAuthorize]
public interface ICliPackageService
{
    /// <summary>
    /// 获取已安装的 CLI 软件包。
    /// </summary>
    [Get]
    Task<IReadOnlyList<CliPackageModel>> GetListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 从远程地址下载并验证 CLI 软件包。
    /// </summary>
    [Post("url")]
    Task<CliPackageModel> InstallFromUrlAsync(InstallCliFromUrlRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 上传并验证 CLI 软件包。
    /// </summary>
    [Post("upload")]
    Task<CliPackageModel> UploadAsync(
        [Form] Frpm.Domain.Enums.ProviderType providerType,
        [Form] string version,
        [Form("file")] RemoteUploadFile file,
        [Form] string? sha256,
        [Form] bool replaceExisting,
        [Form] bool activateAfterInstall,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 激活指定 CLI 软件包，并可选择重启受影响的隧道。
    /// </summary>
    [Post("{id}/activate")]
    Task ActivateAsync([Path] Guid id, [Query] bool restartRunningTunnels, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除未激活的 CLI 软件包；既有运行历史会保留。
    /// </summary>
    [Delete("{id}")]
    Task DeleteAsync([Path] Guid id, CancellationToken cancellationToken = default);
}
