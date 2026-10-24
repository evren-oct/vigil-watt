using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Aula;

/// <summary>
/// 功 能： HERO 68 Air 的 Bytech 查询、8K 分片与应答校验。
/// 2026/10/23 20:05:00 30149
/// 官方来源和真机帧见 docs/hardware/aula.md §2。
/// </summary>
internal static class AulaHero68Codec
{
    public const ushort VendorId = 0x372E;
    public const ushort ProductId = 0x106C;
    public const byte ReportId = 0x09;
    public const int ReportLength = 20;
    public const int PacketLength = 63;
    public const int FragmentPayloadLength = 14;
    public const int MaxFragments = 5;

    public static bool IsSupportedInterface(ushort vid, ushort pid, ushort usagePage, ushort usage,
        ushort inputLength, ushort outputLength, byte? reportId)
        => vid == VendorId && pid == ProductId && usagePage == 0xFF60 && usage == 0x0061
           && inputLength == ReportLength && outputLength == ReportLength && reportId == ReportId;

    public static byte[] CreateUuidQuery() => CreateQuery(0x82, 0x01, 6);
    public static byte[] CreateBatteryQuery() => CreateQuery(0x87, 0, 0);

    private static byte[] CreateQuery(byte command, byte parameter, byte length)
    {
        var packet = new byte[PacketLength + 1];
        packet[0] = ReportId;
        packet[1] = command;
        packet[2] = parameter;
        packet[4] = 1;
        packet[6] = length;
        packet[^1] = (byte)(255 - Sum(packet));
        return packet;
    }

    public static byte[] CreateFragment(ReadOnlySpan<byte> payload, int total, int current, int sync)
    {
        if (payload.Length is < 1 or > FragmentPayloadLength || total is < 1 or > MaxFragments
            || current < 1 || current > total || sync is < 0 or > 7)
            throw new ArgumentOutOfRangeException(nameof(payload));
        var report = new byte[ReportLength];
        report[0] = ReportId;
        report[1] = 0x66;
        report[2] = (byte)(total | ((sync & 4) << 5));
        report[3] = (byte)(current | ((sync & 2) << 6));
        report[4] = (byte)(payload.Length | ((sync & 1) << 7));
        payload.CopyTo(report.AsSpan(5));
        report[5 + payload.Length] = Sum(report.AsSpan(1, 4 + payload.Length));
        return report;
    }

    public static bool TryParseFragment(ReadOnlySpan<byte> report, out int total, out int current,
        out int sync, out int length)
    {
        total = current = sync = length = 0;
        if (report.Length != ReportLength || report[0] != ReportId || report[1] != 0x66) return false;
        total = report[2] & 0x7F;
        current = report[3] & 0x7F;
        length = report[4] & 0x7F;
        sync = ((report[2] & 0x80) >> 5) | ((report[3] & 0x80) >> 6) | ((report[4] & 0x80) >> 7);
        return total is >= 1 and <= MaxFragments && current >= 1 && current <= total
               && length <= FragmentPayloadLength
               && report[5 + length] == Sum(report.Slice(1, 4 + length));
    }

    public static byte[] CreateAck(ReadOnlySpan<byte> report)
    {
        if (!TryParseFragment(report, out _, out _, out _, out int length) || length == 0)
            throw new ArgumentException("Invalid data fragment", nameof(report));
        var ack = new byte[ReportLength];
        report[..5].CopyTo(ack);
        ack[4] &= 0x80;
        ack[5] = Sum(ack.AsSpan(1, 4));
        return ack;
    }

    public static bool IsMatchingAck(ReadOnlySpan<byte> report, ReadOnlySpan<byte> sent)
        => TryParseFragment(report, out _, out _, out _, out int length) && length == 0
           && sent.Length == ReportLength && report[2] == sent[2] && report[3] == sent[3]
           && report[4] == (sent[4] & 0x80);

    public static bool IsHero68Air(ReadOnlySpan<byte> packet)
        => IsResponse(packet, 0x82, 1, 6)
           && packet.Slice(6, 6).SequenceEqual(new byte[] { 0x12, 0, 0, 0, 0, 0x0A });

    public static bool TryParseBattery(ReadOnlySpan<byte> packet, out int percent, out ChargingState charging)
    {
        percent = 0;
        charging = ChargingState.Unknown;
        if (!IsResponse(packet, 0x87, 0, 2)) return false;
        percent = packet[6];
        if (percent > 100) return false;
        byte flag = packet[7];
        charging = (flag & 0xF0) != 0 ? ChargingState.Charging
            : (flag & 0x0F) != 0 ? ChargingState.Full : ChargingState.Discharging;
        return true;
    }

    public static bool IsResponse(ReadOnlySpan<byte> packet, byte command, byte parameter, byte length)
        => packet.Length == PacketLength && packet[0] == command && packet[1] == parameter
           && packet[2] == 0 && packet[3] == 1 && packet[4] == 0 && packet[5] == length
           && ((Sum(packet) + ReportId) & 0xFF) == 0xFF;

    private static byte Sum(ReadOnlySpan<byte> bytes)
    {
        int sum = 0;
        foreach (byte value in bytes) sum += value;
        return (byte)(sum & 0xFF);
    }
}
