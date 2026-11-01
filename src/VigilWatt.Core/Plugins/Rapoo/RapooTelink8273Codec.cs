using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Rapoo;

/// <summary>
/// 功 能： Telink 8273 鼠标（MT 系列）的 `0xBB` 电量上报解析，纯函数不碰句柄。
/// 2026/11/01 22:35:00 xliu
/// 帧布局取自官方 AHUB 2.0 的 `TB()`，见 docs/hardware/rapoo-catalog.md §2；本机无 MT 系列设备，未真机验证。
/// </summary>
internal static class RapooTelink8273Codec
{
    public const byte ReportId = 0xBB;

    /// <summary>载荷首字节固定 <c>0xB0</c>，官方解析器据此确认是电量上报而不是别的 0xBB 帧。</summary>
    public const byte Marker = 0xB0;

    /// <summary>marker + 模式 + CPI(u16) + 有效位 + 电量 = 6 字节（不含 report ID）。</summary>
    private const int PayloadLength = 6;

    /// <summary>
    /// 解析一帧。<c>[4]</c> 是**电量有效位**，不是充电位——无效时官方直接丢弃，
    /// 所以这里返回 null 而不是 0%。官方也没有充电位（`batStatus` 是由有效位/有线推导的），充电态只能报 Unknown。
    /// </summary>
    public static (int Percent, ChargingState Charging)? Parse(byte[] input)
    {
        foreach (int off in new[] { 0, 1 })
        {
            if (input.Length < off + PayloadLength + 1) continue;
            if (input[off] != ReportId) continue;

            int p = off + 1;
            if (input[p] != Marker) continue;
            if (input[p + 4] != 1) return null;

            int pct = input[p + 5];
            if (pct is 0 or > 100) return null;
            return (pct, pct >= 100 ? ChargingState.Full : ChargingState.Unknown);
        }
        return null;
    }
}
