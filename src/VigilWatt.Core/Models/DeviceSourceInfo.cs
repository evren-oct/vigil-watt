namespace VigilWatt.Core.Models;

/// <summary>
/// 功 能： 设备来源的展示元数据（设备列表徽标 / 桌面小组件共用，纯逻辑可单测）。
/// 2026/10/09 13:37:44 xliu
/// 标签是**唯一事实来源**：两个 UI 面都调这里，避免各写一份 switch 而慢慢分叉。
/// </summary>
public static class DeviceSourceInfo
{
    public static DeviceSource FromId(string id) =>
        id.StartsWith("btc:", StringComparison.Ordinal) || id.StartsWith("dualsense:", StringComparison.Ordinal)
            ? DeviceSource.BluetoothClassic
        : id.StartsWith("ble:", StringComparison.Ordinal) ? DeviceSource.Ble
        : id.StartsWith("sys:", StringComparison.Ordinal) ? DeviceSource.System
        : DeviceSource.Hid24G;

    /// <summary>来源徽标短标签：蓝牙 / 2.4G / 本机。</summary>
    public static string ShortLabel(DeviceSource source) => source switch
    {
        DeviceSource.Ble or DeviceSource.BluetoothClassic => "蓝牙",
        DeviceSource.System => "本机",
        _ => "2.4G",
    };
}
