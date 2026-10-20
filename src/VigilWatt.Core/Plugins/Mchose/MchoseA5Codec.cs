using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Mchose;

/// <summary>
/// 功 能： 迈从 A5 V2 Ultra 的反码 Feature 电量协议（与同品牌 E2 状态帧鼠标协议是两条链路）。
/// 2026/10/19 20:38:41 kaniu
/// 帧结构、命令与真机样本见 docs/hardware/protocols.md §14。
/// </summary>
internal static class MchoseA5Codec
{
    public const ushort VendorId = 0x3837;
    public const ushort ProductId = 0x100C;
    public const byte ReportId = 0x11;
    public const int PayloadLength = 20;

    public static byte[] BuildBatteryQueryPayload()
    {
        var payload = new byte[PayloadLength];
        Array.Fill(payload, (byte)0xFF);
        payload[0] = 0xF9;
        return payload;
    }

    public static bool IsBatteryNotification(ReadOnlySpan<byte> frame)
        => frame.Length >= 3 && frame[0] == 0x13 && frame[2] == 0xF9;

    public static bool TryParse(ReadOnlySpan<byte> frame, out int percent, out ChargingState charging)
    {
        percent = 0;
        charging = ChargingState.Unknown;
        if (frame.Length < PayloadLength + 1 || frame[0] != ReportId || frame[1] != 0xF9) return false;
        int vid = (frame[2] ^ 0xFF) | ((frame[3] ^ 0xFF) << 8);
        int pid = (frame[4] ^ 0xFF) | ((frame[5] ^ 0xFF) << 8);
        // 接收器返回鼠标本体的 ID，不是 dongle ID。见 docs/hardware/protocols.md §14。
        if (vid != 0x41E4 || pid is not (0x1101 or 0x1102)) return false;
        int level = frame[11] ^ 0xFF;
        int charge = frame[12] ^ 0xFF;
        if (level > 100 || charge > 2) return false;
        percent = level;
        charging = charge switch
        {
            0 => ChargingState.Discharging,
            1 => ChargingState.Charging,
            2 => ChargingState.Full,
            _ => ChargingState.Unknown,
        };
        return true;
    }
}
