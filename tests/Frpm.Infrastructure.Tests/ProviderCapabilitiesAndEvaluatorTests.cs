using Frpm.Abstractions.Models;
using Frpm.Domain.Enums;
using Xunit;

namespace Frpm.Infrastructure.Tests;

public sealed class ProviderCapabilitiesAndEvaluatorTests
{
    [Fact]
    public void BuiltInProviderCatalog_Defaults_ReflectExactProviderCapabilities()
    {
        var sakura = BuiltInProviderCatalog.Get(ProviderType.SakuraFrp);
        Assert.False(sakura.Capabilities.SupportsKick);
        Assert.False(sakura.Capabilities.SupportsDualProtocol);
        Assert.Equal(3, sakura.Capabilities.MaximumDomains);
        Assert.Contains("wol", sakura.Capabilities.Protocols);
        Assert.True(sakura.Capabilities.SupportsTrafficStats);

        var lolia = BuiltInProviderCatalog.Get(ProviderType.LoliaFrp);
        Assert.False(lolia.Capabilities.SupportsKick);
        Assert.True(lolia.Capabilities.SupportsDualProtocol);
        Assert.Equal(1, lolia.Capabilities.MaximumDomains);
        Assert.Contains("tcp+udp", lolia.Capabilities.Protocols);
        Assert.True(lolia.Capabilities.SupportsTokenReset);
        Assert.True(lolia.Capabilities.SupportsTrafficStats);

        var me = BuiltInProviderCatalog.Get(ProviderType.MeFrp);
        Assert.True(me.Capabilities.SupportsKick);
        Assert.False(me.Capabilities.SupportsDualProtocol);
        Assert.True(me.Capabilities.RequiresCustomHttpDomain);
        Assert.True(me.Capabilities.SupportsTokenReset);
        Assert.True(me.Capabilities.SupportsTrafficStats);
    }

    [Fact]
    public void BuiltInProviderCatalog_ApplyOverrides_MergesJsonProperly()
    {
        try
        {
            var json = """
            {
              "Providers": {
                "LoliaFrp": {
                  "SupportsKick": true,
                  "MaximumDomains": 5
                }
              }
            }
            """;

            BuiltInProviderCatalog.ApplyOverrides(json);
            var updated = BuiltInProviderCatalog.Get(ProviderType.LoliaFrp);
            Assert.True(updated.Capabilities.SupportsKick);
            Assert.Equal(5, updated.Capabilities.MaximumDomains);
            Assert.True(updated.Capabilities.SupportsDualProtocol); // Kept default
        }
        finally
        {
            BuiltInProviderCatalog.ResetOverrides();
        }

        var reset = BuiltInProviderCatalog.Get(ProviderType.LoliaFrp);
        Assert.False(reset.Capabilities.SupportsKick);
        Assert.Equal(1, reset.Capabilities.MaximumDomains);
    }

    [Theory]
    [InlineData("CN", null, null, NodeRegionClassifier.GroupChina)]
    [InlineData(null, "CN", null, NodeRegionClassifier.GroupChina)]
    [InlineData(null, null, "宁波电信 01", NodeRegionClassifier.GroupChina)]
    [InlineData("HK", null, null, NodeRegionClassifier.GroupHongKongMacauTaiwan)]
    [InlineData(null, null, "香港 BGP 高防", NodeRegionClassifier.GroupHongKongMacauTaiwan)]
    [InlineData(null, null, "台北中华电信", NodeRegionClassifier.GroupHongKongMacauTaiwan)]
    [InlineData("JP", null, null, NodeRegionClassifier.GroupAsiaPacific)]
    [InlineData(null, null, "东京直连 02", NodeRegionClassifier.GroupAsiaPacific)]
    [InlineData("SG", null, "新加坡", NodeRegionClassifier.GroupAsiaPacific)]
    [InlineData("US", null, null, NodeRegionClassifier.GroupGlobal)]
    [InlineData(null, null, "洛杉矶 CN2", NodeRegionClassifier.GroupGlobal)]
    [InlineData(null, null, "德国法兰克福", NodeRegionClassifier.GroupGlobal)]
    public void NodeRegionClassifier_ClassifiesCorrectly(string? region, string? regionCode, string? name, string expectedGroup)
    {
        var group = NodeRegionClassifier.Classify(region, regionCode, name);
        Assert.Equal(expectedGroup, group);
    }

    [Fact]
    public void TunnelActionEvaluator_KickRules_EnforcedCorrectly()
    {
        var meFrpCaps = BuiltInProviderCatalog.Get(ProviderType.MeFrp).Capabilities;
        var loliaCaps = BuiltInProviderCatalog.Get(ProviderType.LoliaFrp).Capabilities;

        Assert.True(TunnelActionEvaluator.IsKickVisible(meFrpCaps));
        Assert.False(TunnelActionEvaluator.IsKickVisible(loliaCaps));

        var onlineTunnel = CreateTestTunnel(TunnelRemoteState.Active, TunnelRuntimeState.Stopped);
        Assert.True(TunnelActionEvaluator.CanKick(onlineTunnel, meFrpCaps, out var kickReason1));
        Assert.Null(kickReason1);

        var runningTunnel = CreateTestTunnel(TunnelRemoteState.Inactive, TunnelRuntimeState.Running);
        Assert.True(TunnelActionEvaluator.CanKick(runningTunnel, meFrpCaps, out var kickReason2));
        Assert.Null(kickReason2);

        var offlineTunnel = CreateTestTunnel(TunnelRemoteState.Inactive, TunnelRuntimeState.Stopped);
        Assert.False(TunnelActionEvaluator.CanKick(offlineTunnel, meFrpCaps, out var kickReason3));
        Assert.NotNull(kickReason3);
        Assert.Contains("无需强制下线", kickReason3);

        // LoliaFrp does not support kick
        Assert.False(TunnelActionEvaluator.CanKick(onlineTunnel, loliaCaps, out var kickReason4));
        Assert.Contains("不支持", kickReason4);
    }

    [Fact]
    public void TunnelActionEvaluator_StartStopDeleteRules_EnforcedCorrectly()
    {
        var stoppedTunnel = CreateTestTunnel(TunnelRemoteState.Active, TunnelRuntimeState.Stopped);
        Assert.True(TunnelActionEvaluator.CanStart(stoppedTunnel, out _));
        Assert.False(TunnelActionEvaluator.CanStop(stoppedTunnel, out var stopReason));
        Assert.Contains("未在运行", stopReason);
        Assert.True(TunnelActionEvaluator.CanDelete(stoppedTunnel, out _));

        var runningTunnel = CreateTestTunnel(TunnelRemoteState.Active, TunnelRuntimeState.Running);
        Assert.False(TunnelActionEvaluator.CanStart(runningTunnel, out var startReason));
        Assert.Contains("正在运行中", startReason);
        Assert.True(TunnelActionEvaluator.CanStop(runningTunnel, out _));
        Assert.False(TunnelActionEvaluator.CanDelete(runningTunnel, out var deleteReason));
        Assert.Contains("请先停止", deleteReason);

        var missingTunnel = CreateTestTunnel(TunnelRemoteState.RemoteMissing, TunnelRuntimeState.Stopped);
        Assert.False(TunnelActionEvaluator.CanStart(missingTunnel, out var missingStartReason));
        Assert.Contains("不存在", missingStartReason);
    }

    [Fact]
    public void TunnelActionEvaluator_BatchEvaluator_FiltersEligibleCorrectly()
    {
        var meFrpCaps = BuiltInProviderCatalog.Get(ProviderType.MeFrp).Capabilities;
        var loliaCaps = BuiltInProviderCatalog.Get(ProviderType.LoliaFrp).Capabilities;

        var t1 = CreateTestTunnel(TunnelRemoteState.Active, TunnelRuntimeState.Stopped);
        var t2 = CreateTestTunnel(TunnelRemoteState.Active, TunnelRuntimeState.Running);
        var t3 = CreateTestTunnel(TunnelRemoteState.RemoteMissing, TunnelRuntimeState.Stopped);

        var list = new[] { t1, t2, t3 };

        var (canStart, _, startCount) = TunnelActionEvaluator.EvaluateBatchStart(list);
        Assert.True(canStart);
        Assert.Equal(1, startCount); // Only t1 is stopped and not missing

        var (canStop, _, stopCount) = TunnelActionEvaluator.EvaluateBatchStop(list);
        Assert.True(canStop);
        Assert.Equal(1, stopCount); // Only t2 is running

        // When all are LoliaFrp (no kick support)
        var (_, loliaVisible, _, _) = TunnelActionEvaluator.EvaluateBatchKick(list, _ => loliaCaps);
        Assert.False(loliaVisible);

        // When provider is MeFrp
        var (canKick, meVisible, _, kickCount) = TunnelActionEvaluator.EvaluateBatchKick(list, _ => meFrpCaps);
        Assert.True(meVisible);
        Assert.True(canKick);
        Assert.Equal(2, kickCount); // t1 (active remote) and t2 (running locally)
    }

    [Theory]
    [InlineData(null, "0 B")]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1048576L, "1.0 MB")]
    [InlineData(5368709120L, "5.00 GB")]
    public void TunnelActionEvaluator_FormatBytes_FormatsHumanReadable(long? bytes, string expected)
    {
        var result = TunnelActionEvaluator.FormatBytes(bytes);
        Assert.Equal(expected, result);
    }

    private static TunnelModel CreateTestTunnel(TunnelRemoteState remoteState, TunnelRuntimeState runtimeState) =>
        new(
            Id: Guid.NewGuid(),
            ProviderAccountId: Guid.NewGuid(),
            ProviderAccountName: "TestAccount",
            ProviderType: ProviderType.MeFrp,
            RemoteId: "123",
            Name: "TestTunnel",
            Remark: "Remark",
            Type: "tcp",
            NodeId: "1",
            NodeName: "Node1",
            LocalAddress: "127.0.0.1",
            LocalPort: 8080,
            RemoteAddress: "10001",
            ProviderStatus: "active",
            RemoteState: remoteState,
            DesiredState: TunnelDesiredState.Stopped,
            RuntimeState: runtimeState,
            RuntimeMessage: null,
            UpdatedAt: DateTimeOffset.UtcNow,
            ProviderDomain: null,
            NodeAddress: null);
}
