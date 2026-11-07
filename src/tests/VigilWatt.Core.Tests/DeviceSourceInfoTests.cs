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
    public void S4_徽标优先连接方式_未知才回落来源()
    {
        // 插线的雷柏：来源仍是 Hid24G，但徽标必须写「有线」——拿来源当连接方式是本次修的 bug
        Assert.Equal("有线", DeviceSourceInfo.BadgeLabel(ConnectionKind.Wired, DeviceSource.Hid24G));
        Assert.Equal("2.4G", DeviceSourceInfo.BadgeLabel(ConnectionKind.Wireless24G, DeviceSource.Hid24G));

        // 未知一律回落旧标签：未适配设备零回归
        Assert.Equal("2.4G", DeviceSourceInfo.BadgeLabel(ConnectionKind.Unknown, DeviceSource.Hid24G));
        Assert.Equal("蓝牙", DeviceSourceInfo.BadgeLabel(ConnectionKind.Unknown, DeviceSource.Ble));
        Assert.Equal("本机", DeviceSourceInfo.BadgeLabel(ConnectionKind.Unknown, DeviceSource.System));
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
