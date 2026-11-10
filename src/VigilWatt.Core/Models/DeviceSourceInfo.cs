namespace VigilWatt.Core.Models;

/// <summary>
/// 功 能： 设备**来源**（接入栈）的展示元数据；连接方式不在这里，见 <see cref="DeviceConnectionInfo"/>。
/// 2026/10/09 13:37:44 xliu
/// 来源只回答"数据从哪条接入栈进来"，BLE/经典蓝牙/本机三类同时也是传输事实，其余不是。
/// </summary>
public static class DeviceSourceInfo
{
    /// <summary>按设备 ID 前缀恢复来源。dualsense: 是蓝牙 HID，不是 2.4G。</summary>
    public static DeviceSource FromId(string id) =>
        id.StartsWith("btc:", StringComparison.Ordinal) || id.StartsWith("dualsense:", StringComparison.Ordinal)
            ? DeviceSource.BluetoothClassic
        : id.StartsWith("ble:", StringComparison.Ordinal) ? DeviceSource.Ble
        : id.StartsWith("sys:", StringComparison.Ordinal) ? DeviceSource.System
        : DeviceSource.Hid24G;

    /// <summary>来源徽标短标签：蓝牙 / 2.4G / 本机。**只有前两者与"本机"能当链路用**，Hid24G 是接入栈。</summary>
    public static string ShortLabel(DeviceSource source) => source switch
    {
        DeviceSource.Ble or DeviceSource.BluetoothClassic => "蓝牙",
        DeviceSource.System => "本机",
        _ => "2.4G",
    };
}
