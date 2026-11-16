using VigilWatt.Core.Hid;
using VigilWatt.Core.Plugins.Betop;
using VigilWatt.Core.Plugins.Protocol;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： BetopDeviceIdentity —— 目录 group 驱动的同型号多通道归一。
/// 2026/10/25 17:45:16 lym18
/// 归一动机与缺点见 docs/hardware/brands/betop.md §1.8.2。
/// </summary>
public class BetopDeviceIdentityTests
{
    private static readonly ProtocolEngine Engine = ProtocolEngine.Load(
        "VigilWatt.Core.Plugins.Betop.specs.betop.protocols.json",
        "VigilWatt.Core.Plugins.Betop.betop-catalog.json");

    private static HidDeviceInfo Info(ushort vid, ushort pid, string path, ushort page = 0xFF00)
        => new()
        {
            DevicePath = path,
            VendorId = vid,
            ProductId = pid,
            UsagePage = page,
            Usage = 0x0003,
            InputReportLength = 64,
            OutputReportLength = 64,
        };

    [Fact]
    public void P3_鲲鹏20两通道归一同一ID()
    {
        var dongle = BetopDeviceIdentity.GroupId(Engine,
            Info(0x20BC, 0x5159, @"\\?\HID#VID_20BC&PID_5159&MI_01&Col01#1", 0x00FF), "betop");
        var wired = BetopDeviceIdentity.GroupId(Engine,
            Info(0x20BC, 0x5158, @"\\?\HID#VID_20BC&PID_5158&MI_01&COL01#2"), "betop");

        Assert.NotNull(dongle);
        Assert.Equal("betop:kunpeng20", dongle);
        Assert.Equal(dongle, wired);
    }

    [Fact]
    public void P4_组外型号返回null走路径哈希回退()
    {
        Assert.Null(BetopDeviceIdentity.GroupId(Engine,
            Info(0x20BC, 0x5191, @"\\?\HID#VID_20BC&PID_5191&MI_02&COL04#3"), "betop"));
        Assert.Null(BetopDeviceIdentity.GroupId(Engine,
            Info(0x20BC, 0x5126, @"\\?\HID#VID_20BC&PID_5126&MI_01&COL01#4"), "betop"));
    }

    [Fact]
    public void P5_不同VID相同PID不自动归一()
    {
        Assert.Null(BetopDeviceIdentity.GroupId(Engine,
            Info(0x20BC, 0x5128, @"\\?\HID#VID_20BC&PID_5128&MI_01&COL01#5"), "betop"));
        Assert.Null(BetopDeviceIdentity.GroupId(Engine,
            Info(0x20DD, 0x5128, @"\\?\HID#VID_20DD&PID_5128&MI_01&COL01#6", 0x00FF), "betop"));
    }
}
