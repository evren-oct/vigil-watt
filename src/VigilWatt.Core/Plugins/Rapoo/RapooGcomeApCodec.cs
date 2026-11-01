using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Rapoo;

/// <summary>
/// 功 能： GCOME_AP 键盘（Ace 68 Turbo / VT68）的电量帧解析，纯函数不碰句柄。
/// 2026/11/01 22:35:00 xliu
/// 帧取自官方 AHUB 2.0 的 `Sk()`：`FA FB 07 ?? <充电码> <电量>`，见 docs/hardware/rapoo-catalog.md §2；本机无键盘，未真机验证。
/// </summary>
internal static class RapooGcomeApCodec
{
    private const byte Head0 = 0xFA;
    private const byte Head1 = 0xFB;
    private const byte KindBattery = 0x07;

    /// <summary>
    /// 解析一帧。充电码 <c>&gt;0</c> 即充电（官方 `charging: chargingCode &gt; 0`），
    /// 具体档位含义没有标签表，只当布尔用。
    /// </summary>
    public static (int Percent, ChargingState Charging)? Parse(byte[] input)
    {
        foreach (int off in new[] { 0, 1 })
        {
            if (input.Length < off + 6) continue;
            if (input[off] != Head0 || input[off + 1] != Head1 || input[off + 2] != KindBattery) continue;

            int chargeCode = input[off + 4];
            int pct = input[off + 5];
            if (pct is 0 or > 100) return null;

            var charging = pct >= 100 ? ChargingState.Full
                : chargeCode > 0 ? ChargingState.Charging
                : ChargingState.Discharging;
            return (pct, charging);
        }
        return null;
    }
}
