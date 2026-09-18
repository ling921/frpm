namespace Frpm.Abstractions.Services;

/// <summary>
/// 管理内置 FRP 供应商账号及其远端资源同步。
/// </summary>
[RemoteService("/api/provider-accounts")]
[RemoteAuthorize]
public interface IProviderAccountService
{
    /// <summary>
    /// 获取全部供应商账号。
    /// </summary>
    [Get]
    Task<IReadOnlyList<ProviderAccountModel>> GetListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证并保存供应商账号。
    /// </summary>
    [Post]
    Task<ProviderAccountModel> SaveAsync(SaveProviderAccountRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除供应商账号及其本地记录。
    /// </summary>
    [Delete("{id}")]
    Task DeleteAsync([Path] Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 立即同步指定账号的远端隧道。
    /// </summary>
    [Post("{id}/sync")]
    Task SyncAsync([Path] Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定账号可用的供应商节点。
    /// </summary>
    [Get("{id}/nodes")]
    Task<IReadOnlyList<ProviderNodeModel>> GetNodesAsync([Path] Guid id, CancellationToken cancellationToken = default);
}
