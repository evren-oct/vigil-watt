namespace VigilWatt.Core.Plugins.Ganss;

/// <summary>
/// 功 能： GS3104T-Pro 已验证电量报文的构造与解析。
/// 2026/10/18 20:20:00 路泽
/// </summary>
public static class GanssCodec
{
    public const int PayloadLength = 32;
    public const int HidReportLength = 33;

    public static bool IsSupportedInterface(ushort vendorId, ushort productId, string path,
        ushort usagePage, ushort usage, ushort inputLength, ushort outputLength, byte? reportId)
        => vendorId == 0x05AC && productId == 0x024F
           && path.Contains("&mi_03#", StringComparison.OrdinalIgnoreCase)
           && usagePage == 0xFF60 && usage == 0x0061
           && inputLength == HidReportLength && outputLength == HidReportLength
           && reportId == 0;

    public static byte[] CreateQuery()
    {
        var report = new byte[HidReportLength];
        report[1] = 0x20;
        report[2] = 0x01;
        report[^1] = 0x21;
        return report;
    }

    public static bool TryParse(ReadOnlySpan<byte> report, out int percent)
    {
        percent = 0;
        if (report.Length == HidReportLength)
        {
            if (report[0] != 0) return false;
            report = report[1..];
        }
        if (report.Length != PayloadLength || report[0] != 0x20
            || report[1] != 0x01 || report[2] != 0 || report[3] is 0 or > 100)
            return false;
        int checksum = 0;
        foreach (byte value in report[..^1]) checksum += value;
        if ((byte)checksum != report[^1]) return false;
        percent = report[3];
        return true;
    }
}
