using Frpm.Domain.Enums;

namespace Frpm.Abstractions.Models;

/// <summary>
/// 管理界面使用的隧道状态快照。
/// </summary>
public sealed record TunnelModel(
    Guid Id, Guid ProviderAccountId, string ProviderAccountName, ProviderType ProviderType,
    string RemoteId, string Name, string? Remark, string Type, string? NodeId, string? NodeName,
    string? LocalAddress, int? LocalPort, string? RemoteAddress, string? ProviderStatus,
    TunnelRemoteState RemoteState, TunnelDesiredState DesiredState, TunnelRuntimeState RuntimeState,
    string? RuntimeMessage, DateTimeOffset UpdatedAt, string? ProviderDomain, string? NodeAddress);

/// <summary>
/// 创建或更新供应商隧道所需的参数。
/// </summary>
public sealed class SaveTunnelRequest
{
    public Guid ProviderAccountId { get; set; }
    public Guid? TunnelId { get; set; }
    public string? RemoteId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Remark { get; set; }
    public string Type { get; set; } = "tcp";
    public string? NodeId { get; set; }
    public string LocalAddress { get; set; } = "127.0.0.1";
    public int? LocalPort { get; set; }
    public int? RemotePort { get; set; }
    public string? CustomDomain { get; set; }
    public Dictionary<string, string?> AdvancedOptions { get; set; } = [];
}

/// <summary>
/// 仪表盘显示的隧道汇总数据。
/// </summary>
public sealed record DashboardModel(int AccountCount, int TunnelCount, int RunningCount, int FailedCount, int RemoteMissingCount);

/// <summary>
/// 一次本地 CLI 运行的历史摘要。
/// </summary>
public sealed record TunnelRunModel(
    Guid Id, Guid TunnelId, string TunnelName, int? ProcessId, string CliVersion,
    DateTimeOffset StartedAt, DateTimeOffset? EndedAt, int? ExitCode, string? Summary);

/// <summary>
/// 从指定游标读取的增量日志片段。
/// </summary>
public sealed record LogTailModel(Guid RunId, long NextCursor, IReadOnlyList<string> Lines, bool EndOfFile);

/// <summary>
/// 待测试的本地服务端点。
/// </summary>
public sealed record LocalEndpointTestRequest(string Address, int? Port, string TunnelType);

/// <summary>
/// 本地服务端点可访问性测试结果。
/// </summary>
public sealed record LocalEndpointTestResult(
    bool Reachable,
    bool Conclusive,
    string TestedAddress,
    int? Port,
    long? ElapsedMilliseconds,
    string Message);
