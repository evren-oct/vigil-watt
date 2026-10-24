using VigilWatt.Core.Models;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： ClassicDeviceKindResolver 用例；C1 是"RAPOO BT3.0 鼠标被认成耳机"的回归。
/// 2026/10/09 17:22:33 xliu
/// 类别值与服务 UUID 取自真机实测。
/// </summary>
public class ClassicDeviceKindResolverTests
{
    [Fact]
    public void C1_雷柏BT3鼠标不再被认成耳机()
    {
        // 真机 BTHENUM 父节点：DEVPKEY_DeviceDisplay_Category = ["Input.Mouse"]
        Assert.Equal(DeviceKind.Mouse, ClassicDeviceKindResolver.Resolve(
            "RAPOO BT3.0 Mouse", new[] { "Input.Mouse" }, Array.Empty<string>()));
    }

    [Fact]
    public void C2_只有HID服务且名字认不出时退回Unknown而不是耳机()
    {
        // 旧代码在此硬编码 Headset（本 bug 的根因）；{00001124}=HIDP
        Assert.Equal(DeviceKind.Unknown, ClassicDeviceKindResolver.Resolve(
            "Rapoo Gaming Device", Array.Empty<string>(), new[] { "00001124", "00001200" }));
    }

    [Fact]
    public void C3_有HID服务时靠名字区分鼠标与键盘()
    {
        Assert.Equal(DeviceKind.Mouse, ClassicDeviceKindResolver.Resolve(
            "RAPOO BT3.0 Mouse", null, new[] { "00001124" }));
        Assert.Equal(DeviceKind.Keyboard, ClassicDeviceKindResolver.Resolve(
            "RAPOO BT Keyboard", null, new[] { "00001124" }));
    }

    [Fact]
    public void C4_音频服务优先于名字_无关键字的耳机也认成耳机()
    {
        Assert.Equal(DeviceKind.Headset, ClassicDeviceKindResolver.Resolve(
            "EDIFIER W820NB 双金标版", Array.Empty<string>(), new[] { "0000111e", "0000110b", "00001200" }));
        Assert.Equal(DeviceKind.Headset, ClassicDeviceKindResolver.Resolve(
            null, null, new[] { "00001108" }));
    }

    [Fact]
    public void C5_音频设备名字说是音箱就是音箱()
    {
        Assert.Equal(DeviceKind.Speaker, ClassicDeviceKindResolver.Resolve(
            "某蓝牙音箱", null, new[] { "0000110b" }));
    }

    [Theory]
    [InlineData("Input.Mouse", DeviceKind.Mouse)]
    [InlineData("Input.Trackball", DeviceKind.Mouse)]
    [InlineData("Input.Keyboard", DeviceKind.Keyboard)]
    [InlineData("Input.Gaming.Gamepad", DeviceKind.Gamepad)]
    [InlineData("Input.Gaming.Steering", DeviceKind.Gamepad)]
    [InlineData("Audio.Headphone", DeviceKind.Headset)]
    [InlineData("Communication.Headset.Bluetooth", DeviceKind.Headset)]
    [InlineData("Audio.Speakers.Wireless", DeviceKind.Speaker)]
    [InlineData("Input.Digitizer.Touchpad", DeviceKind.Unknown)]
    [InlineData("Network.Bluetooth", DeviceKind.Unknown)]
    [InlineData("Other", DeviceKind.Unknown)]
    public void C6_Windows设备类别映射(string category, DeviceKind expected)
    {
        Assert.Equal(expected, ClassicDeviceKindResolver.Resolve("某设备", new[] { category }, Array.Empty<string>()));
    }

    [Fact]
    public void C7_设备类别优先于服务UUID()
    {
        // 多模设备（元数据包说鼠标、同时又暴露 A2DP）时以类别为准
        Assert.Equal(DeviceKind.Mouse, ClassicDeviceKindResolver.Resolve(
            "RAPOO BT3.0 Mouse", new[] { "Input.Mouse" }, new[] { "0000110b" }));
    }

    [Fact]
    public void C8_全无线索时Unknown()
    {
        Assert.Equal(DeviceKind.Unknown, ClassicDeviceKindResolver.Resolve("未知设备", Array.Empty<string>(), Array.Empty<string>()));
        Assert.Equal(DeviceKind.Unknown, ClassicDeviceKindResolver.Resolve(null, null, null));
    }

    [Fact]
    public void C9_协议描述按服务UUID给出()
    {
        Assert.Equal("经典蓝牙 A2DP/HFP", ClassicDeviceKindResolver.DescribeProtocol(new[] { "0000111e" }));
        Assert.Equal("经典蓝牙 HIDP", ClassicDeviceKindResolver.DescribeProtocol(new[] { "00001124", "00001200" }));
        Assert.Equal("经典蓝牙", ClassicDeviceKindResolver.DescribeProtocol(Array.Empty<string>()));
        Assert.Equal("经典蓝牙", ClassicDeviceKindResolver.DescribeProtocol(null));
    }
}
