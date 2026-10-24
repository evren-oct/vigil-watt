using VigilWatt.Core.Models;
using Xunit;

namespace VigilWatt.Core.Tests;

    /// <summary>
    /// 功 能： DeviceKindGuesser —— 按显示名猜设备类型（插件没声明 Kind 时的兜底）。
    /// 2026/10/06 15:53:36 xliu
    /// 用例含真机显示名：它们**一个关键字都没有**，正是"靠名字猜不可靠"的证据。
    /// </summary>
    public class DeviceKindGuesserTests
{
    [Fact]
    public void K1_手柄优先于键盘鼠标()
    {
        // 真机：北通的 USB 产品串含 "Keyboard & Mouse"，若先匹配 keyboard/mouse 就会认成键盘
        Assert.Equal(DeviceKind.Gamepad, DeviceKindGuesser.Guess("BEITONG T9 For Keyboard & Mouse"));
        Assert.Equal(DeviceKind.Gamepad, DeviceKindGuesser.Guess("北通 T9"));
        Assert.Equal(DeviceKind.Gamepad, DeviceKindGuesser.Guess("Xbox Wireless Controller"));
    }

    [Fact]
    public void K2_真机显示名一个关键字都没有_只能靠插件声明()
    {
        Assert.Equal(DeviceKind.Unknown, DeviceKindGuesser.Guess("雷柏 Rapoo Gaming Device"));
        Assert.Equal(DeviceKind.Unknown, DeviceKindGuesser.Guess("MCHOSE 2.4G Wireless"));
        Assert.Equal(DeviceKind.Unknown, DeviceKindGuesser.Guess("EDIFIER W820NB 双金标版"));
    }

    [Fact]
    public void K3_带关键字的常规名字能猜对()
    {
        Assert.Equal(DeviceKind.Mouse, DeviceKindGuesser.Guess("MX Master 3 Mouse"));
        Assert.Equal(DeviceKind.Mouse, DeviceKindGuesser.Guess("雷柏鼠标"));
        Assert.Equal(DeviceKind.Keyboard, DeviceKindGuesser.Guess("Keychron K8 Keyboard"));
        Assert.Equal(DeviceKind.Keyboard, DeviceKindGuesser.Guess("迈从键盘"));
        Assert.Equal(DeviceKind.Headset, DeviceKindGuesser.Guess("WH-1000XM5 Headphones"));
        Assert.Equal(DeviceKind.Headset, DeviceKindGuesser.Guess("AirPods Pro"));
        Assert.Equal(DeviceKind.Speaker, DeviceKindGuesser.Guess("某蓝牙音箱"));
    }

    [Fact]
    public void K5_罗技MX系列产品名不含mouse也能认成鼠标()
    {
        Assert.Equal(DeviceKind.Mouse, DeviceKindGuesser.Guess("MX Master 3S"));
        Assert.Equal(DeviceKind.Mouse, DeviceKindGuesser.Guess("MX Anywhere 3"));
        Assert.Equal(DeviceKind.Mouse, DeviceKindGuesser.Guess("G Pro X Superlight"));
    }

    [Fact]
    public void K4_空名字返回Unknown()
    {
        Assert.Equal(DeviceKind.Unknown, DeviceKindGuesser.Guess(null));
        Assert.Equal(DeviceKind.Unknown, DeviceKindGuesser.Guess(""));
        Assert.Equal(DeviceKind.Unknown, DeviceKindGuesser.Guess("   "));
        Assert.Equal(DeviceKind.Unknown, DeviceKindGuesser.Guess("某不认识的设备"));
    }

    [Fact]
    public void K6_过宽的head关键字不再误判()
    {
        // 旧版耳机关键字里有裸 "head"，"Ahead"/"Headway" 这类会被认成耳机
        Assert.Equal(DeviceKind.Unknown, DeviceKindGuesser.Guess("Ahead Workstation"));
        Assert.Equal(DeviceKind.Unknown, DeviceKindGuesser.Guess("Headway HUB"));
        // 真耳机名仍然认得出
        Assert.Equal(DeviceKind.Headset, DeviceKindGuesser.Guess("Sony WH-1000XM5 Headphones"));
        Assert.Equal(DeviceKind.Headset, DeviceKindGuesser.Guess("Soundcore Earbuds"));
    }
}
