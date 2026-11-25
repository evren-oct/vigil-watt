using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Logitech;

/// <summary>
/// 功 能： 构造和解析 PRO X 2 LIGHTSPEED 的只读电池报文。
/// 2026/10/23 20:58:09 nyamu
/// 协议依据见 docs/hardware/brands/logitech.md §2。
/// </summary>
internal static class CenturionCodec
{
    public const int ReportLength = 64;
    public const ushort LogitechVid = 0x046D;
    public const ushort UsagePage = 0xFFA0;
    public const ushort Usage = 0x0001;
    public const byte ReportId = 0x51;
    public const byte BatteryReplyMarker = 0x04;

    public static bool IsSupportedInterface(
        ushort vendorId, ushort productId, ushort usagePage, ushort usage,
        ushort inputLength, ushort outputLength)
        => vendorId == LogitechVid
           && productId == 0x0AF7
           && usagePage == UsagePage
           && usage == Usage
           && inputLength >= ReportLength
           && outputLength >= ReportLength;

    public static byte[] BuildBatteryRequest()
    {
        var packet = new byte[ReportLength];
        packet[0] = ReportId;
        packet[1] = 0x08;
        packet[3] = 0x03;
        packet[4] = 0x1A;
        packet[6] = 0x03;
        packet[8] = BatteryReplyMarker;
        packet[9] = 0x0A;
        return packet;
    }

    public static bool TryParseBatteryResponse(byte[]? packet, out int percent, out ChargingState charging)
    {
        percent = 0;
        charging = ChargingState.Unknown;
        if (packet is null || packet.Length < 13
            || packet[0] != ReportId
            || packet[1] != 0x0B
            || packet[8] != BatteryReplyMarker
            || packet[10] > 100)
            return false;

        percent = packet[10];
        charging = packet[12] switch
        {
            0x00 => ChargingState.Discharging,
            0x02 => ChargingState.Charging,
            _ => ChargingState.Unknown,
        };
        return true;
    }
}
