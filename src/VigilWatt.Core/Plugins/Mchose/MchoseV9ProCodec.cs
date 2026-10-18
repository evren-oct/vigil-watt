namespace VigilWatt.Core.Plugins.Mchose;

/// <summary>
/// 功 能： V9 Pro 耳机固定电量查询的报文构造与解析。
/// 2026/10/19 00:32:00 路泽
/// </summary>
public static class MchoseV9ProCodec
{
    public const int ReportLength = 64;
    public const byte ReportId = 0x55;

    public static bool IsSupportedInterface(ushort vendorId, ushort productId, string path,
        ushort usagePage, ushort usage, ushort inputLength, ushort outputLength,
        ushort featureLength, byte? reportId)
        => vendorId == 0x291D && productId == 0x385D
           && path.Contains("&mi_00&col05#", StringComparison.OrdinalIgnoreCase)
           && usagePage == 0xFF90 && usage == 0x0001
           && inputLength == ReportLength && outputLength == ReportLength
           && featureLength == 0 && reportId == ReportId;

    public static byte[] CreateQuery()
    {
        var report = new byte[ReportLength];
        report[0] = ReportId;
        report[1] = 0x65;
        report[2] = 0x01;
        return report;
    }

    public static bool TryParse(ReadOnlySpan<byte> report, out int percent)
    {
        percent = 0;
        if (report.Length != ReportLength || report[0] != ReportId
            || report[1] != 0x65 || report[2] is 0 or > 100)
            return false;
        percent = report[2];
        return true;
    }
}
