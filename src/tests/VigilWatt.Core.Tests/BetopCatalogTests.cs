using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Betop;
using VigilWatt.Core.Plugins.Protocol;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 北通品牌目录——PID 命中、型号歧义、显示名、身份隔离与能力声明。
/// 2026/11/16 12:00:00 20211
/// 目录来源与逐型号验证状态见 docs/hardware/reference/betop-catalog.md。
/// </summary>
public class BetopCatalogTests
{
    private static readonly ProtocolEngine Engine = ProtocolEngine.Load(
        "VigilWatt.Core.Plugins.Betop.specs.betop.protocols.json",
        "VigilWatt.Core.Plugins.Betop.betop-catalog.json");

    private static readonly (ushort Vid, ushort Pid, ushort Page, string Model)[] Rows =
    [
        (0x20BC, 0x5073, 0xFF00, "宙斯2"),
        (0x20BC, 0x5125, 0x00FF, ""),
        (0x20BC, 0x5126, 0x00FF, "鲲鹏20智控游戏手柄"),
        (0x20BC, 0x5127, 0x00FF, ""),
        (0x20BC, 0x5128, 0x00FF, "鲲鹏20智控游戏手柄"),
        (0x20BC, 0x5158, 0x00FF, "鲲鹏20 绝尘白"),
        (0x20BC, 0x5159, 0x00FF, "鲲鹏20 绝尘白"),
        (0x20BC, 0x5191, 0x00FF, "鲲鹏20精英版-丁未"),
        (0x20BC, 0x5145, 0x00FF, ""),
        (0x20BC, 0x5146, 0x00FF, ""),
        (0x20BC, 0x5154, 0x00FF, "鲲鹏40 明末联动款"),
        (0x20BC, 0x5155, 0x00FF, "鲲鹏40 明末联动款"),
        (0x20BC, 0x515B, 0x00FF, "鲲鹏40 御铠白"),
        (0x20BC, 0x515C, 0x00FF, "鲲鹏40 御铠白"),
        (0x20BC, 0x515D, 0x00FF, "鲲鹏40智控游戏手柄 风驰白"),
        (0x20BC, 0x515E, 0x00FF, "鲲鹏40智控游戏手柄 风驰白"),
        (0x20BC, 0x5169, 0x00FF, "鲲鹏40 电掣黑"),
        (0x20BC, 0x516A, 0x00FF, "鲲鹏40 电掣黑"),
        (0x20BC, 0x5133, 0x00FF, "鲲鹏50 云翼白"),
        (0x20BC, 0x5149, 0x00FF, "鲲鹏50 星穹紫"),
        (0x20BC, 0x5150, 0x00FF, "鲲鹏50智控游戏手柄 解限机联名款"),
        (0x20BC, 0x5151, 0x00FF, "鲲鹏50智控游戏手柄 解限机联名款"),
        (0x20BC, 0x5152, 0x00FF, "鲲鹏50智控游戏手柄 解限机联名款"),
        (0x20BC, 0x5153, 0x00FF, "鲲鹏50智控游戏手柄 解限机联名款"),
        (0x20BC, 0x5134, 0x00FF, "鲲鹏50 云翼白"),
        (0x20BC, 0x514A, 0x00FF, "鲲鹏50 星穹紫"),
        (0x20BC, 0x512F, 0x00FF, "鲲鹏70 AI旗舰"),
        (0x20BC, 0x5130, 0x00FF, "鲲鹏70 AI旗舰"),
        (0x20BC, 0x515F, 0x00FF, "鲲鹏70AI智控旗舰手柄"),
        (0x20BC, 0x5160, 0x00FF, "鲲鹏70AI智控旗舰手柄"),
        (0x20DD, 0x5128, 0x00FF, "鲲鹏20智控游戏手柄"),
        (0x20DD, 0x5130, 0x00FF, "鲲鹏70 AI旗舰"),
        (0x20DD, 0x515C, 0x00FF, "鲲鹏40 御铠白"),
    ];

    public static IEnumerable<object[]> CatalogPids
        => Rows.Select(row => new object[] { row.Vid, row.Pid, row.Page, row.Model });

    private static HidDeviceInfo Info(ushort vid, ushort pid, ushort page = 0xFF00, string product = "")
        => new()
        {
            DevicePath = $"test-{vid:X4}-{pid:X4}",
            VendorId = vid,
            ProductId = pid,
            UsagePage = page,
            Usage = 0x0003,
            InputReportLength = 64,
            OutputReportLength = 64,
            Product = product,
        };

    [Fact]
    public void Catalog_CountsAndProtocolsLoad()
    {
        Assert.Equal(33, Engine.DeviceCount);
        Assert.Equal(2, Engine.ProtocolCount);
        Assert.NotNull(Engine.SpecOf("betop-status15"));
        Assert.NotNull(Engine.SpecOf("betop-t9"));
    }

    [Theory]
    [MemberData(nameof(CatalogPids))]
    public void Catalog_EveryPidResolves(ushort vid, ushort pid, ushort page, string model)
    {
        var info = Info(vid, pid, page);
        var entry = Engine.DeviceOf(info);

        Assert.NotNull(entry);
        Assert.True(Engine.Matches(info));
        Assert.Equal(model, entry!.Model);
    }

    [Fact]
    public void Catalog_SamePidDifferentVidIsDistinct()
    {
        // 2026-11-16 起 devices[].model 用官方标题，同一 PID 的两套身份同名 → 改用 role 区分身份。
        Assert.Equal("controller", Engine.DeviceOf(Info(0x20BC, 0x5128, 0x00FF))!.Role);
        Assert.Equal("dongle", Engine.DeviceOf(Info(0x20DD, 0x5128, 0x00FF))!.Role);
        Assert.Equal("controller", Engine.DeviceOf(Info(0x20BC, 0x5130, 0x00FF))!.Role);
        Assert.Equal("dock", Engine.DeviceOf(Info(0x20DD, 0x5130, 0x00FF))!.Role);
        Assert.Equal("controller", Engine.DeviceOf(Info(0x20BC, 0x515C, 0x00FF))!.Role);
        Assert.Equal("dongle", Engine.DeviceOf(Info(0x20DD, 0x515C, 0x00FF))!.Role);

        Assert.Equal("鲲鹏20智控游戏手柄", Engine.DeviceOf(Info(0x20DD, 0x5128, 0x00FF))!.Model);
        Assert.Equal("鲲鹏70 AI旗舰", Engine.DeviceOf(Info(0x20DD, 0x5130, 0x00FF))!.Model);
        Assert.Equal("鲲鹏40 御铠白", Engine.DeviceOf(Info(0x20DD, 0x515C, 0x00FF))!.Model);
    }

    [Fact]
    public void Catalog_RolesLoad()
    {
        Assert.Equal("controller", Engine.DeviceOf(Info(0x20BC, 0x5073, 0xFF00))!.Role);
        Assert.Equal("controller", Engine.DeviceOf(Info(0x20BC, 0x5158, 0x00FF))!.Role);
        Assert.Equal("dongle", Engine.DeviceOf(Info(0x20BC, 0x5134, 0x00FF))!.Role);
        Assert.Equal("dongle", Engine.DeviceOf(Info(0x20DD, 0x5128, 0x00FF))!.Role);
        Assert.Equal("dock", Engine.DeviceOf(Info(0x20DD, 0x5130, 0x00FF))!.Role);
    }

    [Theory]
    [InlineData("BTP-KP20A/BTP-KP40A Controller", "鲲鹏20/40")]
    [InlineData("BTP-KP20A Controller", "鲲鹏20")]
    [InlineData("BTP-KP40A Controller", "鲲鹏40")]
    [InlineData("BETOP BTP-KP40D Controller (White)", "鲲鹏40")]
    [InlineData("BEITONG T9 For Keyboard & Mouse", "T9")]
    [InlineData("BETOP T90 Controller", null)]
    public void Display_AmbiguousPidUsesProductString(string product, string? expectedModel)
    {
        Assert.Equal(expectedModel, BetopDisplayName.ModelFromProduct(product));
    }

    [Fact]
    public void Display_CatalogModelWinsAndSuffixesDoNotBecomeModel()
    {
        var t9 = Info(0x20BC, 0x5073, 0xFF00, "BEITONG T9 For Keyboard & Mouse");
        Assert.Equal("北通 宙斯2", BetopDisplayName.For(t9, Engine.DeviceOf(t9)));

        var kp20 = Info(0x20BC, 0x5159, 0x00FF, "BTP-KP20D XINPUT DONGLE");
        Assert.Equal("北通 鲲鹏20 绝尘白", BetopDisplayName.For(kp20, Engine.DeviceOf(kp20)));

        var dongle = Info(0x20DD, 0x5128, 0x00FF, "KP20 DONGLE");
        Assert.Equal("北通 鲲鹏20智控游戏手柄", BetopDisplayName.For(dongle, Engine.DeviceOf(dongle)));

        var unknown = Info(0x20BC, 0x9999, 0xFF00, "BEITONG XF1 For Keyboard & Mouse");
        Assert.Equal("北通手柄 (VID_20BC&PID_9999)", BetopDisplayName.For(unknown, Engine.DeviceOf(unknown)));
    }

    [Fact]
    public void Identity_OnlyExplicitGroupIsMerged()
    {
        var wired = Info(0x20BC, 0x5158, 0x00FF);
        var wireless = Info(0x20BC, 0x5159, 0x00FF);
        Assert.Equal("betop:kunpeng20", BetopDeviceIdentity.GroupId(Engine, wired, "betop"));
        Assert.Equal("betop:kunpeng20", BetopDeviceIdentity.GroupId(Engine, wireless, "betop"));

        Assert.Null(BetopDeviceIdentity.GroupId(Engine, Info(0x20BC, 0x5126, 0x00FF), "betop"));
        Assert.Null(BetopDeviceIdentity.GroupId(Engine, Info(0x20BC, 0x5128, 0x00FF), "betop"));
        Assert.Null(BetopDeviceIdentity.GroupId(Engine, Info(0x20DD, 0x5128, 0x00FF), "betop"));
    }

    [Fact]
    public void Capabilities_OnlyVerifiedEntriesDeclareBatteryRead()
    {
        foreach (var row in Rows)
        {
            var info = Info(row.Vid, row.Pid, row.Page);
            var expected = (row.Vid, row.Pid) is (0x20BC, 0x5073) or (0x20BC, 0x5158) or (0x20BC, 0x5159)
                ? CapabilityState.Supported
                : CapabilityState.Unverified;
            Assert.Equal(expected, Engine.CapabilityOf(info, DeviceCapability.BatteryRead));
        }

        Assert.Equal(CapabilityState.Unsupported,
            Engine.CapabilityOf(Info(0x20BC, 0x5073, 0xFF00), DeviceCapability.ChargingRead));
        Assert.Equal(CapabilityState.Supported,
            Engine.CapabilityOf(Info(0x20BC, 0x5158, 0x00FF), DeviceCapability.ChargingRead));
        Assert.Equal(CapabilityState.Supported,
            Engine.CapabilityOf(Info(0x20BC, 0x5159, 0x00FF), DeviceCapability.ChargingRead));
    }

    [Fact]
    public void UnknownPidFallsBackAndUnknownVendorIsRejected()
    {
        var unknownBetop = Info(0x20BC, 0x9999, 0xFF00);
        Assert.Null(Engine.DeviceOf(unknownBetop));
        Assert.True(Engine.Matches(unknownBetop));
        Assert.Null(BetopDeviceIdentity.GroupId(Engine, unknownBetop, "betop"));

        Assert.False(Engine.Matches(Info(0x1234, 0x9999, 0xFF00)));
    }
}
