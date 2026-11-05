using Frpm.Abstractions.Models;
using Frpm.Domain.Enums;

namespace Frpm.Application;

public sealed record CliInstallPlan(bool ReplaceExisting, bool ActivateAfterInstall, string? Confirmation);

public static class CliInstallationPolicy
{
    public static CliInstallPlan Plan(IEnumerable<CliPackageModel> packages, ProviderType provider, string version,
        SystemSettingsModel environment, bool activate)
    {
        var target = packages.Where(p => p.ProviderType == provider && p.OperatingSystem == environment.OperatingSystem && p.Architecture == environment.Architecture).ToList();
        var existing = target.FirstOrDefault(p => p.Version.Equals(version.Trim(), StringComparison.OrdinalIgnoreCase));
        var active = target.FirstOrDefault(p => p.IsActive);
        var downgrade = active is not null && CompareVersions(version, active.Version) < 0;
        var messages = new List<string>();
        if (existing is not null) messages.Add($"{version} 已安装，将替换该版本的文件；若正在使用会重启相关隧道。");
        if (downgrade) messages.Add($"将从活动版本 {active!.Version} 降级到 {version} 并重启相关隧道。");
        return new(existing is not null, activate || downgrade, messages.Count == 0 ? null : string.Join(Environment.NewLine, messages));
    }

    private static int CompareVersions(string left, string right)
    {
        static int[]? Parts(string value)
        {
            var numeric = new string(value.Trim().TrimStart('v', 'V').TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
            var values = numeric.Split('.');
            return values.All(v => int.TryParse(v, out _)) ? values.Select(int.Parse).ToArray() : null;
        }
        var a = Parts(left);
        var b = Parts(right);
        if (a is null || b is null) return 0;
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var comparison = (i < a.Length ? a[i] : 0).CompareTo(i < b.Length ? b[i] : 0);
            if (comparison != 0) return comparison;
        }
        return 0;
    }
}
