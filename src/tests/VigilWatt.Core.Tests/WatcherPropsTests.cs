using VigilWatt.Core.Ble;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： WatcherProps —— BLE / 经典蓝牙两个 watcher 共用的属性读取（D1 / CR-13）。
/// 2026/10/10 17:44:33 xliu
/// 重点锁"属性缺失返回 null 而不是 false"：这是 2026-10-06 那次 P0 的根因形态 （Updated 事件只带变化的属性，默认 false 会把已连接设备误判离线）。
/// </summary>
public class WatcherPropsTests
{
    [Theory]
    [InlineData("", "GS3104T PRO", "GS3104T PRO")]
    [InlineData(null, "GS3104T PRO", "GS3104T PRO")]
    [InlineData("  ", " GS3104T PRO ", "GS3104T PRO")]
    [InlineData("Existing keyboard", "GS3104T PRO", "Existing keyboard")]
    [InlineData("", null, "")]
    public void DeviceNameFallbackDoesNotOverwriteKnownName(string? current, string? resolved, string expected)
        => Assert.Equal(expected, WatcherProps.ResolveName(current, resolved));

    private static Dictionary<string, object> Props(params (string Key, object Value)[] items)
    {
        var d = new Dictionary<string, object>();
        foreach (var (k, v) in items) d[k] = v;
        return d;
    }

    [Fact]
    public void W1_属性缺失返回null_不能当成断开()
    {
        // Updated 事件只带变化的属性：IsConnected 不在袋里 ≠ 断开
        Assert.Null(WatcherProps.ReadConnected(Props((WatcherProps.Name, "耳机"))));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void W2_属性存在时按布尔解析(bool raw, bool expected)
    {
        Assert.Equal(expected, WatcherProps.ReadConnected(Props((WatcherProps.Connected, raw))));
    }

    [Fact]
    public void W3_属性类型不对返回null而不是抛异常()
    {
        Assert.Null(WatcherProps.ReadConnected(Props((WatcherProps.Connected, "not-a-bool"))));
    }

    [Fact]
    public void W4_电量属性名未探明时返回null()
    {
        Assert.Null(WatcherProps.ReadBattery(Props(("x", 50)), batteryProp: null));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    public void W5_有效电量原样返回(int raw, int expected)
    {
        Assert.Equal(expected, WatcherProps.ReadBattery(Props(("b", raw)), "b"));
    }

    [Theory]
    [InlineData(0)]      // 0 是占位值，不是有效读数
    [InlineData(101)]
    [InlineData(-1)]
    public void W6_越界电量视为无读数(int raw)
    {
        Assert.Null(WatcherProps.ReadBattery(Props(("b", raw)), "b"));
    }

    [Fact]
    public void W7_电量类型不对返回null而不是抛异常()
    {
        Assert.Null(WatcherProps.ReadBattery(Props(("b", "eighty")), "b"));
    }

    [Fact]
    public void W8_电量属性缺失返回null()
    {
        Assert.Null(WatcherProps.ReadBattery(Props(("other", 50)), "b"));
    }
}
