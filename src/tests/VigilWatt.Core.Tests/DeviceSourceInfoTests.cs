using VigilWatt.Core.Models;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： DeviceSourceInfo —— 来源徽标短标签。
/// 2026/10/09 13:37:44 xliu
/// 设备列表页与桌面小组件都调它， 这里固化映射，防止两个 UI 面各写一份 switch 后慢慢分叉。
/// </summary>
public class DeviceSourceInfoTests
{
    [Theory]
    [InlineData("dualsense:123", DeviceSource.BluetoothClassic)]
    [InlineData("btc:123", DeviceSource.BluetoothClassic)]
    [InlineData("ble:123", DeviceSource.Ble)]
    [InlineData("sys:battery", DeviceSource.System)]
    [InlineData("ganss:123", DeviceSource.Hid24G)]
    public void HistorySourceMatchesLiveTransport(string id, DeviceSource expected)
        => Assert.Equal(expected, DeviceSourceInfo.FromId(id));

    [Fact]
    public void S1_三种来源各有标签()
    {
        Assert.Equal("蓝牙", DeviceSourceInfo.ShortLabel(DeviceSource.Ble));
        Assert.Equal("蓝牙", DeviceSourceInfo.ShortLabel(DeviceSource.BluetoothClassic));
        Assert.Equal("2.4G", DeviceSourceInfo.ShortLabel(DeviceSource.Hid24G));
        Assert.Equal("本机", DeviceSourceInfo.ShortLabel(DeviceSource.System));
    }

    [Fact]
    public void S2_标签非空_且三种来源互不重复()
    {
        var labels = Enum.GetValues<DeviceSource>().Select(DeviceSourceInfo.ShortLabel).ToList();
        Assert.All(labels, l => Assert.False(string.IsNullOrWhiteSpace(l)));
        // BLE 与经典蓝牙刻意共用「蓝牙」标签，所以去重后是 3 个（蓝牙 / 2.4G / 本机）
        Assert.Equal(3, labels.Distinct().Count());
    }
}
