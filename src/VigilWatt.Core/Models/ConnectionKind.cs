namespace VigilWatt.Core.Models;

/// <summary>
/// 功 能： 设备**当前的数据链路**（有线 / 2.4G / 蓝牙），与"来源"（接入栈）是两件事。
/// 2026/11/07 20:50:00 xliu
/// 只描述链路，不描述是否在充电：插着线但仍走 2.4G 时就是 2.4G。接收器体系（Bolt / LIGHTSPEED）不是链路，不进这里。
/// </summary>
public enum ConnectionKind
{
    /// <summary>未知：帧里没有该字段、值没有语义，或设备不属于这类链路。默认值。**"没有证据"由空遥测表达，不是靠这个值**。</summary>
    Unknown = 0,

    /// <summary>有线：数据走 USB 线。</summary>
    Wired,

    /// <summary>2.4G：数据走厂商接收器/无线链路。</summary>
    Wireless24G,

    /// <summary>蓝牙：数据走 BLE 或经典蓝牙。与 <see cref="Wireless24G"/> 是两条不同链路，不合并。</summary>
    Bluetooth,
}
