using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Aula;

/// <summary>
/// 功 能： 解析狼蛛 G9 耳机 AB 47 4D 0x86 状态帧里的电量。
/// 2026/10/19 23:50:00 contributor
/// 帧布局与固件前置条件见 docs/hardware/protocols.md §13。
/// </summary>
internal static class AulaG9Codec
{
    /// <summary>该通道的 report ID。</summary>
    public const byte ReportId = 0xAB;

    /// <summary>周期状态帧命令（每秒一条）。</summary>
    private const byte CmdStatus = 0x86;

    /// <summary>报文含 reportId，故电量在下标 6、充电标志在下标 7。</summary>
    private const int LevelIndex = 6;
    private const int ChargingIndex = 7;

    /// <summary>
    /// 解析一条状态帧。返回 false = 不是本协议帧。
    /// <paramref name="percent"/> 为 null 表示"帧在但没读数"（旧固件电量字段恒 0）。
    /// </summary>
    public static bool TryParse(byte[] input, out int? percent, out ChargingState charging)
    {
        percent = null;
        charging = ChargingState.Unknown;

        if (input.Length <= ChargingIndex) return false;
        if (input[0] != ReportId || input[1] != 0x47 || input[2] != 0x4D || input[3] != CmdStatus)
            return false;

        byte level = input[LevelIndex];
        if (level > 100) return false;

        byte flag = input[ChargingIndex];
        // 固件 V0.0.4 的签名：电量 0 + 标志 0 = 没在报数，不是真空电
        if (level == 0 && flag == 0) return true;

        percent = level;
        charging = ChargingStateMap.FromChargingFlag(flag == 0x02, level);
        return true;
    }
}
