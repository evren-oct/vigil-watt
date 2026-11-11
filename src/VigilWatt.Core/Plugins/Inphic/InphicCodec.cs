using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Inphic;

/// <summary>
/// 功 能： 解析英菲克 report 0x03 电量状态帧。
/// 2026/10/14 20:41:17 20211
/// 帧布局见 docs/hardware/brands/inphic.md §1。
/// </summary>
internal static class InphicCodec
{
    public const byte StatusReportId = 0x03;
    public const byte StatusMarker0 = 0x76;
    public const byte StatusMarker1 = 0x41;

    public static bool TryParse(byte[] input, out int percent, out ChargingState charging, out byte chargeFlag)
    {
        percent = 0;
        charging = ChargingState.Unknown;
        chargeFlag = 0;

        int offset = input.Length > 0 && input[0] == StatusReportId ? 1 : 0;
        if (input.Length < offset + 4 || input[offset] != StatusMarker0 || input[offset + 1] != StatusMarker1)
            return false;

        chargeFlag = input[offset + 2];
        byte rawPercent = input[offset + 3];
        if (rawPercent > 100) return false;

        percent = rawPercent;
        if (chargeFlag == 0x01) charging = ChargingState.Discharging;
        else if (chargeFlag == 0x03) charging = ChargingState.Charging;
        return true;
    }
}
