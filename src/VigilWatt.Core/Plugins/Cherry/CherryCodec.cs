using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Cherry;

/// <summary>
/// 功 能： CHERRY MX 8.2 Xaga 接收器电量帧的构造与解析。
/// 2026/10/19 20:38:38 kaniu
/// </summary>
public static class CherryCodec
{
    public const ushort VendorId = 0x046A;
    public const ushort ProductId = 0x01C3;
    public const byte ReportId = 0x04;
    public const int ReportLength = 64;

    public static byte[] BuildBatteryQuery()
    {
        var frame = new byte[ReportLength];
        frame[0] = ReportId;
        frame[1] = 0x20;
        frame[3] = 0x1A;
        frame[4] = 6;
        return frame;
    }

    public static bool TryParse(ReadOnlySpan<byte> frame, out int percent, out ChargingState charging)
    {
        percent = 0;
        charging = ChargingState.Unknown;
        if (frame.Length != ReportLength || frame[0] != ReportId
            || frame[1] != 0x20 || frame[2] != 0 || frame[3] != 0x1A
            || frame[4] != 6 || frame[5] != 0 || frame[6] != 0 || frame[7] != 0
            || frame[8] > 100) return false;

        percent = frame[8];
        // 非零供电状态尚未接线校准，不能把它猜成充电或充满。见 protocols.md §13。
        charging = frame[9] == 0 ? ChargingState.Discharging : ChargingState.Unknown;
        return true;
    }
}
