using VigilWatt.Core.Plugins.Betop;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： BetopDeviceIdentity —— 鲲鹏20 dongle/有线本体的同型号组身份归一。
/// 2026/10/25 17:45:16 lym18
/// 归一动机与缺点见 docs/hardware/brands/betop.md §1.8.2。
/// </summary>
public class BetopDeviceIdentityTests
{
    // ---------------------------------------------------------------- PID 提取

    [Fact]
    public void P1_从HID路径提取PID_大小写与接口段不敏感()
    {
        Assert.Equal(0x5158, BetopDeviceIdentity.PidFromPath(
            @"\\?\HID#VID_20BC&PID_5158&MI_01&COL01#7&282C4991&0&0000"));
        Assert.Equal(0x5159, BetopDeviceIdentity.PidFromPath(
            @"\\?\HID#VID_20BC&PID_5159&MI_01&Col01#7&2ff3aabf&0&0000"));
        // 大写 PID 前缀（个别枚举路径变体）
        Assert.Equal(0x5159, BetopDeviceIdentity.PidFromPath(
            @"\\?\HID#VID_20BC&PID_5159&IG_01#8&232C3F8&0&0000"));
    }

    [Fact]
    public void P2_无PID路径返回0()
    {
        Assert.Equal(0, BetopDeviceIdentity.PidFromPath(@"\\?\HID#VID_20BC#7&282C4991&0&0000"));
    }

    // ---------------------------------------------------------------- ID 归一

    [Fact]
    public void P3_鲲鹏20两通道归一同一ID()
    {
        var dongle = BetopDeviceIdentity.KunpengId(
            @"\\?\HID#VID_20BC&PID_5159&MI_01&Col01#7&2ff3aabf&0&0000", "betop");
        var wired = BetopDeviceIdentity.KunpengId(
            @"\\?\HID#VID_20BC&PID_5158&MI_01&COL01#7&282C4991&0&0000", "betop");
        Assert.NotNull(dongle);
        Assert.Equal(dongle, wired);
        Assert.Equal("betop:kunpeng20", dongle);
    }

    [Fact]
    public void P4_组外型号返回null走路径哈希回退()
    {
        // T9 等其它北通型号不在组内：返回 null，由插件回退 base.IdFor 路径哈希
        Assert.Null(BetopDeviceIdentity.KunpengId(
            @"\\?\HID#VID_20BC&PID_5191&MI_02&COL04#6&1a2b3c4d&0&0004", "betop"));
        // 取不到 PID（返回 0）也不在组内
        Assert.Null(BetopDeviceIdentity.KunpengId(@"\\?\HID#VID_20BC#x", "betop"));
    }
}
