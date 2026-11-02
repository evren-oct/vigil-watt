using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Rapoo;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 雷柏未收录设备的显示名启发式——产品串不带键鼠信息时靠兄弟接口补 Mouse。
/// 2026/11/01 23:20:00 xliu
/// 已收录设备走目录里的官方型号，这条路径只兜未收录机型，见 docs/hardware/rapoo.md §4.2。
/// </summary>
public sealed class RapooDisplayNameTests
{
    [Fact]
    public void R15_键盘集合挂在鼠标设备上时不能显示成键盘()
    {
        // 真机：VT2S Air Max 有线，认领的 FF00:0002 接口产品串是 "Rapoo keyboard"，
        // 但同 VID:PID 下有 "Rapoo Mouse" 兄弟接口 → 这是一只鼠标。
        var name = RapooDisplayName.For("Rapoo keyboard", siblingIsMouse: true);
        Assert.Equal("雷柏 Rapoo Mouse", name);
        Assert.Equal(DeviceKind.Mouse, DeviceKindGuesser.Guess(name));
    }

    [Fact]
    public void R16_真键盘仍显示键盘()
    {
        var name = RapooDisplayName.For("Rapoo keyboard", siblingIsMouse: false);
        Assert.Equal("雷柏 Rapoo keyboard", name);
        Assert.Equal(DeviceKind.Keyboard, DeviceKindGuesser.Guess(name));
    }

    [Fact]
    public void R17_产品串本身是Mouse时直接用()
    {
        var name = RapooDisplayName.For("Rapoo Mouse", siblingIsMouse: true);
        Assert.Equal("雷柏 Rapoo Mouse", name);
        Assert.Equal(DeviceKind.Mouse, DeviceKindGuesser.Guess(name));
    }

    [Fact]
    public void R18_无产品串时退回品牌加VIDPID()
        => Assert.Equal("雷柏 (VID_24AE&PID_4B13)",
            RapooDisplayName.For("", siblingIsMouse: true, vidPid: "VID_24AE&PID_4B13"));

    [Fact]
    public void R21_无线模式设备级产品串也按鼠标命名()
    {
        // 无线（1B13）所有接口产品串都是 "Rapoo Gaming Device"，不含 mouse 关键字。
        var name = RapooDisplayName.For("Rapoo Gaming Device", siblingIsMouse: true);
        Assert.Equal("雷柏 Rapoo Mouse", name);
        Assert.Equal(DeviceKind.Mouse, DeviceKindGuesser.Guess(name));
    }

    [Fact]
    public void R22_设备级产品串在非鼠标设备上保持原样()
        => Assert.Equal("雷柏 Rapoo Gaming Device",
            RapooDisplayName.For("Rapoo Gaming Device", siblingIsMouse: false));
}
