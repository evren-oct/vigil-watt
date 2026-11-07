namespace VigilWatt.Core.Models;

/// <summary>
/// 功 能： 设备**当前的数据链路**（有线 / 2.4G），与"来源"（接入栈）是两件事。
/// 2026/11/07 20:50:00 xliu
/// 只描述链路，不描述是否在充电：插着线但仍走 2.4G 时就是 2.4G。
/// </summary>
public enum ConnectionKind
{
    /// <summary>未知：未适配、帧里没有该字段，或设备不属于这类链路（蓝牙/本机）。默认值。</summary>
    Unknown = 0,

    /// <summary>有线：数据走 USB 线。</summary>
    Wired,

    /// <summary>2.4G：数据走厂商接收器/无线链路。</summary>
    Wireless24G,
}
