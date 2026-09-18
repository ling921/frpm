namespace Frpm.Abstractions.Models;

/// <summary>
/// 系统设置、当前运行环境及供应商同步调度状态。
/// </summary>
public sealed record SystemSettingsModel(
    int ProviderSyncIntervalSeconds,
    Guid InstanceId,
    string OperatingSystem,
    string OperatingSystemDescription,
    string Architecture,
    string FrameworkDescription,
    string ApplicationVersion,
    DateTimeOffset FirstStartedAt,
    DateTimeOffset LastStartedAt,
    long UptimeSeconds,
    DateTimeOffset? LastProviderSyncAt,
    DateTimeOffset? NextProviderSyncAt);

/// <summary>
/// 可由管理员动态修改的系统设置。
/// </summary>
public sealed record SaveSystemSettingsRequest(int ProviderSyncIntervalSeconds);
