namespace VigilWatt.Core.Plugins.Akg;

/// <summary>
/// 功 能： 校验 N9 Hybrid 接口和电量消息，构造固定查询。
/// 2026/10/23 21:07:10 30149
/// 协议来源与真机样本见 docs/hardware/akg.md §1。
/// </summary>
internal static class AkgN9Codec
{
    public const int ReportLength = 62;
    public const int MaxPayloadLength = 59;
    public const int MaxMessageLength = 1024;
    public const byte InputReportId = 0x07;

    public static bool IsSupportedInterface(ushort vid, ushort pid, string path,
        ushort usagePage, ushort usage, int inputLength, int outputLength, int featureLength, byte? outputId)
        => vid == 0x0ECB && pid == 0x20D9 && usagePage == 0xFF13 && usage == 1
           && inputLength == ReportLength && outputLength == ReportLength && featureLength == 0
           && outputId == 0x06 && path.Contains("&mi_03&col02#", StringComparison.OrdinalIgnoreCase);

    public static byte[] CreateBatteryQuery()
    {
        var report = new byte[ReportLength];
        new byte[] { 0x06, 0x07, 0x80, 0x05, 0x5A, 0x03, 0x00, 0xD6, 0x0C, 0x00 }.CopyTo(report, 0);
        return report;
    }

    public static bool TryGetPayloadLength(ReadOnlySpan<byte> report, out int length)
    {
        length = 0;
        if (report.Length != ReportLength || report[0] != InputReportId
            || report[2] != 0x80 || report[1] > MaxPayloadLength) return false;
        length = report[1];
        return true;
    }

    public static bool IsBatteryMessage(ReadOnlySpan<byte> message)
        => message.Length >= 6 && message[0] == 5
           && (message[1] is 0x5B or 0x5C or 0x5D)
           && message.Length == 4 + message[2] + (message[3] << 8)
           && message[4] == 0xD6 && message[5] == 0x0C;

    public static bool IsSuccessfulAck(ReadOnlySpan<byte> message)
        => IsBatteryMessage(message) && message.Length == 7 && message[1] == 0x5B && message[6] == 0;

    public static bool TryParseBattery(ReadOnlySpan<byte> message, out int percent)
    {
        percent = 0;
        if (!IsBatteryMessage(message) || message.Length != 9 || message[1] != 0x5D
            || message[6] != 0 || message[7] != 0 || message[8] > 100) return false;
        percent = message[8];
        return true;
    }
}
