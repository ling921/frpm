using Frpm.Domain.Enums;

namespace Frpm.Abstractions.Models;

public static class TunnelActionEvaluator
{
    public static bool IsKickVisible(ProviderCapabilitiesModel capabilities) =>
        capabilities.SupportsKick;

    public static bool CanKick(TunnelModel tunnel, ProviderCapabilitiesModel capabilities, out string? reason)
    {
        if (!capabilities.SupportsKick)
        {
            reason = "当前供应商不支持强制下线。";
            return false;
        }

        var isOnlineOrRunning = tunnel.RemoteState == TunnelRemoteState.Active
            || tunnel.RuntimeState == TunnelRuntimeState.Running
            || tunnel.RuntimeState == TunnelRuntimeState.Failed;

        if (!isOnlineOrRunning)
        {
            reason = "远端未在线且本地未运行，无需强制下线。";
            return false;
        }

        reason = null;
        return true;
    }

    public static bool CanStart(TunnelModel tunnel, out string? reason)
    {
        if (tunnel.RuntimeState is TunnelRuntimeState.Running or TunnelRuntimeState.Starting)
        {
            reason = "隧道正在运行中。";
            return false;
        }

        if (tunnel.RemoteState == TunnelRemoteState.RemoteMissing)
        {
            reason = "远端隧道不存在，无法启动。";
            return false;
        }

        reason = null;
        return true;
    }

    public static bool CanStop(TunnelModel tunnel, out string? reason)
    {
        if (tunnel.RuntimeState is not (TunnelRuntimeState.Running or TunnelRuntimeState.Starting))
        {
            reason = "隧道未在运行。";
            return false;
        }

        reason = null;
        return true;
    }

    public static bool CanDelete(TunnelModel tunnel, out string? reason)
    {
        if (tunnel.RuntimeState is TunnelRuntimeState.Running or TunnelRuntimeState.Starting)
        {
            reason = "隧道正在运行中，请先停止再删除。";
            return false;
        }

        reason = null;
        return true;
    }

    public static bool CanEdit(TunnelModel tunnel, out string? reason)
    {
        if (tunnel.RuntimeState is TunnelRuntimeState.Running or TunnelRuntimeState.Starting)
        {
            reason = "隧道正在运行中，建议先停止再修改。";
            return false;
        }

        reason = null;
        return true;
    }

    public static (bool CanExecute, string? Reason, int EligibleCount) EvaluateBatchStart(IEnumerable<TunnelModel> targets)
    {
        var targetList = targets as IReadOnlyList<TunnelModel> ?? targets.ToList();
        if (targetList.Count == 0) return (false, "未选择任何隧道。", 0);
        var eligible = targetList.Count(t => CanStart(t, out _));
        if (eligible == 0) return (false, "所选隧道均处于运行中或不可用状态。", 0);
        return (true, null, eligible);
    }

    public static (bool CanExecute, string? Reason, int EligibleCount) EvaluateBatchStop(IEnumerable<TunnelModel> targets)
    {
        var targetList = targets as IReadOnlyList<TunnelModel> ?? targets.ToList();
        if (targetList.Count == 0) return (false, "未选择任何隧道。", 0);
        var eligible = targetList.Count(t => CanStop(t, out _));
        if (eligible == 0) return (false, "所选隧道均未在运行。", 0);
        return (true, null, eligible);
    }

    public static (bool CanExecute, bool IsVisible, string? Reason, int EligibleCount) EvaluateBatchKick(
        IEnumerable<TunnelModel> targets,
        Func<TunnelModel, ProviderCapabilitiesModel> getCapabilities)
    {
        var targetList = targets as IReadOnlyList<TunnelModel> ?? targets.ToList();
        if (targetList.Count == 0) return (false, false, "未选择任何隧道。", 0);

        var anySupportsKick = targetList.Any(t => IsKickVisible(getCapabilities(t)));
        if (!anySupportsKick) return (false, false, "所选隧道供应商均不支持强制下线。", 0);

        var eligible = targetList.Count(t => CanKick(t, getCapabilities(t), out _));
        if (eligible == 0) return (false, true, "所选隧道远端均未在线且本地未运行。", 0);

        return (true, true, null, eligible);
    }

    public static (bool CanExecute, string? Reason, int EligibleCount) EvaluateBatchDelete(IEnumerable<TunnelModel> targets)
    {
        var targetList = targets as IReadOnlyList<TunnelModel> ?? targets.ToList();
        if (targetList.Count == 0) return (false, "未选择任何隧道。", 0);
        var eligible = targetList.Count(t => CanDelete(t, out _));
        return (true, null, eligible);
    }

    public static string FormatBytes(long? bytes)
    {
        if (bytes is null or 0) return "0 B";
        var b = bytes.Value;
        if (b < 1024) return $"{b} B";
        if (b < 1024 * 1024) return $"{(b / 1024.0):F1} KB";
        if (b < 1024 * 1024 * 1024) return $"{(b / (1024.0 * 1024)):F1} MB";
        return $"{(b / (1024.0 * 1024 * 1024)):F2} GB";
    }
}
