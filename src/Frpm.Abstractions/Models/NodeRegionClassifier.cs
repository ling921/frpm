namespace Frpm.Abstractions.Models;

public static class NodeRegionClassifier
{
    public const string GroupAll = "ALL";
    public const string GroupChina = "CN";
    public const string GroupHongKongMacauTaiwan = "HK_MO_TW";
    public const string GroupAsiaPacific = "APAC";
    public const string GroupGlobal = "GLOBAL";

    public static string Classify(string? region, string? regionCode = null, string? nodeName = null)
    {
        var token = (regionCode ?? region ?? string.Empty).Trim().ToUpperInvariant();
        if (token is "CN" or "CHN" or "ZH" or "MAINLAND") return GroupChina;
        if (token is "HK" or "HKG" or "MO" or "MAC" or "TW" or "TWN") return GroupHongKongMacauTaiwan;
        if (token is "JP" or "JPN" or "SG" or "SGP" or "KR" or "KOR" or "MY" or "MYS" or "TH" or "THA" or "IN" or "IND" or "VN" or "VNM") return GroupAsiaPacific;
        if (token is "US" or "USA" or "CA" or "CAN" or "GB" or "UK" or "DE" or "DEU" or "FR" or "FRA" or "RU" or "RUS" or "AU" or "AUS" or "EU") return GroupGlobal;

        var text = $"{region} {nodeName}";
        if (text.Contains("香港", StringComparison.OrdinalIgnoreCase)
            || text.Contains("澳门", StringComparison.OrdinalIgnoreCase)
            || text.Contains("台湾", StringComparison.OrdinalIgnoreCase)
            || text.Contains("台北", StringComparison.OrdinalIgnoreCase))
        {
            return GroupHongKongMacauTaiwan;
        }

        if (text.Contains("日本", StringComparison.OrdinalIgnoreCase)
            || text.Contains("东京", StringComparison.OrdinalIgnoreCase)
            || text.Contains("大阪", StringComparison.OrdinalIgnoreCase)
            || text.Contains("新加坡", StringComparison.OrdinalIgnoreCase)
            || text.Contains("首尔", StringComparison.OrdinalIgnoreCase)
            || text.Contains("韩国", StringComparison.OrdinalIgnoreCase))
        {
            return GroupAsiaPacific;
        }

        if (text.Contains("美国", StringComparison.OrdinalIgnoreCase)
            || text.Contains("欧洲", StringComparison.OrdinalIgnoreCase)
            || text.Contains("德国", StringComparison.OrdinalIgnoreCase)
            || text.Contains("英国", StringComparison.OrdinalIgnoreCase)
            || text.Contains("加拿大", StringComparison.OrdinalIgnoreCase)
            || text.Contains("俄罗斯", StringComparison.OrdinalIgnoreCase)
            || text.Contains("海外", StringComparison.OrdinalIgnoreCase)
            || text.Contains("洛杉矶", StringComparison.OrdinalIgnoreCase)
            || text.Contains("硅谷", StringComparison.OrdinalIgnoreCase)
            || text.Contains("法兰克福", StringComparison.OrdinalIgnoreCase))
        {
            return GroupGlobal;
        }

        return GroupChina;
    }

    public static string GetGroupName(string group) => group switch
    {
        GroupChina => "🇨🇳 中国大陆",
        GroupHongKongMacauTaiwan => "🇭🇰 港澳台",
        GroupAsiaPacific => "🌏 亚太其他",
        GroupGlobal => "🌐 欧美及海外",
        _ => "全部"
    };

    public static IReadOnlyList<(string GroupId, string DisplayName)> DisplayGroups { get; } =
    [
        (GroupChina, "🇨🇳 中国大陆"),
        (GroupHongKongMacauTaiwan, "🇭🇰 港澳台"),
        (GroupAsiaPacific, "🌏 亚太其他"),
        (GroupGlobal, "🌐 欧美及海外")
    ];

    public static int GetDisplayOrder(string? groupId) => groupId switch
    {
        GroupChina => 1,
        GroupHongKongMacauTaiwan => 2,
        GroupAsiaPacific => 3,
        GroupGlobal => 4,
        _ => 5
    };

    public static IReadOnlyList<(string GroupId, string DisplayName)> AllGroups { get; } =
    [
        (GroupAll, "全部地区"),
        (GroupChina, "🇨🇳 中国大陆"),
        (GroupHongKongMacauTaiwan, "🇭🇰 港澳台"),
        (GroupAsiaPacific, "🌏 亚太其他"),
        (GroupGlobal, "🌐 欧美及海外")
    ];
}
