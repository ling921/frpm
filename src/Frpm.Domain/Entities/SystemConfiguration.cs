namespace Frpm.Domain.Entities;

/// <summary>
/// 持久化的单实例系统配置和最近一次运行环境。
/// </summary>
public sealed class SystemConfiguration
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public Guid InstanceId { get; set; } = Guid.NewGuid();
    public int ProviderSyncIntervalSeconds { get; set; } = 60;
    public string OperatingSystem { get; set; } = string.Empty;
    public string OperatingSystemDescription { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string FrameworkDescription { get; set; } = string.Empty;
    public string ApplicationVersion { get; set; } = string.Empty;
    public DateTimeOffset FirstStartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastStartedAt { get; set; } = DateTimeOffset.UtcNow;
}
