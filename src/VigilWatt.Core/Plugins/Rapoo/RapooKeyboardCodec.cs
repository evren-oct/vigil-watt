using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Rapoo;

/// <summary>
/// 功 能： 雷柏键盘的 user-data 帧解析（载荷首字节 <c>0x01</c> 是帧类型标记），纯函数不碰句柄。
/// 2026/11/01 22:35:00 xliu
/// 布局取自官方 AHUB 2.0 的 `parseUserCustomData` / `parseV700UserCustomData`，见 docs/hardware/rapoo-catalog.md §2；
/// 本机无键盘，**未真机验证**，偏移按"hidraw 含 report ID"视角给。
/// </summary>
internal static class RapooKeyboardCodec
{
    /// <summary>帧类型标记：载荷首字节 <c>0x01</c> = 用户数据（`dispatchEvent` 的 `case 1`）。</summary>
    public const byte FrameMarker = 0x01;

    /// <summary>
    /// `TelinkKeyboard`（A83 Air 等）：`[0]` 高4位=profile、低4位=连接方式，`[1]` 背光模式，
    /// `[2]` 亮度，`[3]` 速度，`[4]` 颜色，`[5]` 休眠，`[6]` 回报率，**`[7]` 电量**（均为载荷视角）。
    /// </summary>
    private const int TelinkPercentOffset = 7;

    /// <summary>V700diy 变体少一档：`[0]` profile、`[1]` 模式、`[2]` 速度、`[3]` 亮度、`[4]` 颜色、`[5]` 充电位、**`[6]` 电量**。</summary>
    private const int V700PercentOffset = 6;

    /// <summary>V700diy 的 6 个 PID（`V700diy-75/87/98` 与 LRV 变体），布局与普通 Telink 键盘差一位。</summary>
    private static readonly HashSet<ushort> V700Pids =
        [0x4805, 0x4806, 0x4807, 0x4824, 0x6204, 0x7204];

    /// <summary>
    /// `CH585M`（ESK900 系列）：`[0]` profile、`[1]` 模式、`[2]` 速度、`[3]` 亮度、`[4]` 休眠、
    /// `[5]` 回报率（低4/高4）、`[6..7]` 位域（充电位在 bit14..15）、**`[8]` 电量**。
    /// </summary>
    private const int Ch585mPercentOffset = 8;

    public static bool IsV700(ushort productId) => V700Pids.Contains(productId);

    /// <summary>Telink 键盘（含 V700diy 变体）。电量越界一律丢弃——偏移错一位时这是唯一能挡住脏值的闸。</summary>
    public static (int Percent, ChargingState Charging)? ParseTelink(byte[] input, ushort productId)
    {
        int want = IsV700(productId) ? V700PercentOffset : TelinkPercentOffset;
        foreach (int off in new[] { 0, 1 })
        {
            if (input.Length < off + want + 2) continue;
            if (input[off] != FrameMarker) continue;

            int pct = input[off + 1 + want];
            if (pct is 0 or > 100) continue;
            return (pct, pct >= 100 ? ChargingState.Full : ChargingState.Unknown);
        }
        return null;
    }

    /// <summary>CH585M 键盘。充电位在 <c>[6..7]</c> 的 bit14..15，但取值含义未校准，充电态先报 Unknown。</summary>
    public static (int Percent, ChargingState Charging)? ParseCh585m(byte[] input)
    {
        foreach (int off in new[] { 0, 1 })
        {
            if (input.Length < off + Ch585mPercentOffset + 2) continue;
            if (input[off] != FrameMarker) continue;

            int pct = input[off + 1 + Ch585mPercentOffset];
            if (pct is 0 or > 100) continue;
            return (pct, pct >= 100 ? ChargingState.Full : ChargingState.Unknown);
        }
        return null;
    }
}
