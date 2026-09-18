namespace Frpm.Abstractions.Services;

/// <summary>
/// 提供隧道运行日志的增量读取能力。
/// </summary>
[RemoteService("/api/logs")]
[RemoteAuthorize]
public interface ILogService
{
    /// <summary>
    /// 从指定游标开始读取一次运行产生的新日志。
    /// </summary>
    [Get("{runId}/tail")]
    Task<LogTailModel> TailAsync([Path] Guid runId, [Query] long cursor = 0, [Query] int maxLines = 300, CancellationToken cancellationToken = default);
}
