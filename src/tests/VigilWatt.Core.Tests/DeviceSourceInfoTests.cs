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
    [Fact]
    public void S1_三种来源各有标签()
    {
        Assert.Equal("蓝牙", DeviceSourceInfo.ShortLabel(DeviceSource.Ble));
        Assert.Equal("蓝牙", DeviceSourceInfo.ShortLabel(DeviceSource.BluetoothClassic));
        Assert.Equal("2.4G", DeviceSourceInfo.ShortLabel(DeviceSource.Hid24G));
        Assert.Equal("本机", DeviceSourceInfo.ShortLabel(DeviceSource.System));
    }

    [Fact]
    public void S3_dualsense前缀恢复为蓝牙()
    {
        Assert.Equal(DeviceSource.BluetoothClassic, DeviceSourceInfo.FromId("dualsense:abc"));
        Assert.Equal(DeviceSource.BluetoothClassic, DeviceSourceInfo.FromId("btc:abc"));
        Assert.Equal(DeviceSource.Hid24G, DeviceSourceInfo.FromId("hecate:abc"));
    }

    [Fact]
    public void S4_来源不再冒充连接方式()
    {
        // 无值时只有"来源本身就是链路"的三类可以兜底：BLE / 经典蓝牙 / 本机
        Assert.Equal("蓝牙", DeviceConnectionInfo.Label(default, DeviceSource.Ble));
        Assert.Equal("蓝牙", DeviceConnectionInfo.Label(default, DeviceSource.BluetoothClassic));
        Assert.Equal("本机", DeviceConnectionInfo.Label(default, DeviceSource.System));

        // Hid24G 只是接入栈：插着线的设备也在它下面，不能推断成 2.4G（M4-3A 审计 P1）
        Assert.Equal("未知", DeviceConnectionInfo.Label(default, DeviceSource.Hid24G));
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
