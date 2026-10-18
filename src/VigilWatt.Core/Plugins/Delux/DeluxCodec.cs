using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Delux;

/// <summary>
/// 功 能： DELUX M900 Pro 5 字节状态帧的纯解析（report 0x03：30 40 &lt;flag&gt; &lt;percent&gt;）。
/// 2026/10/19 02:20:00 sqcxz
/// 真机帧：03 30 40 01 59（89% 放电）/ 03 30 40 03 59（89% 充电）。
/// </summary>
internal static class DeluxCodec
{
    public const byte StatusReportId = 0x03;
    public const int FrameLength = 5;
    public const byte StatusMarker0 = 0x30;
    public const byte StatusMarker1 = 0x40;

    public const byte FlagDischarging = 0x01;
    public const byte FlagFull = 0x02;
    public const byte FlagCharging = 0x03;

    /// <summary>解析一帧；兼容带/不带 report ID 的缓冲。返回 false = 不是本协议帧。</summary>
    public static bool TryParse(byte[] input, out int percent, out ChargingState charging, out byte chargeFlag)
    {
        percent = 0;
        charging = ChargingState.Unknown;
        chargeFlag = 0;

        int offset = input.Length > 0 && input[0] == StatusReportId ? 1 : 0;
        if (input.Length < offset + 4 ||
            input[offset] != StatusMarker0 || input[offset + 1] != StatusMarker1)
            return false;

        chargeFlag = input[offset + 2];
        byte rawPercent = input[offset + 3];
        if (rawPercent > 100) return false;

        percent = rawPercent;
        charging = chargeFlag switch
        {
            FlagDischarging => ChargingState.Discharging,
            FlagCharging => ChargingState.Charging,
            FlagFull => ChargingState.Full,
            _ => ChargingState.Unknown,
        };

        // 满电时个别固件仍报"充电中"；以 100% 为准，避免托盘一直显示充电。
        if (percent >= 100 && charging != ChargingState.Discharging)
            charging = ChargingState.Full;

        return true;
    }
}
