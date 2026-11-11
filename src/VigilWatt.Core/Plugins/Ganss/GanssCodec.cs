namespace VigilWatt.Core.Plugins.Ganss;

/// <summary>
/// 功 能： 0x05AC:024F + 0xFF60/0x0061 + 33 字节这一族键盘的电量报文构造与解析（GANSS GS3104T-Pro / 狼蛛 F108 Pro）。
/// 2026/10/19 19:45:00 xliu；2026/10/21 扩展 F108 Pro（同硅方案，只差产品名）。
/// 协议与验证范围见 docs/hardware/brands/ganss.md；两品牌同指纹的陷阱见 docs/hardware/protocols.md §2。
/// </summary>
public static class GanssCodec
{
    /// <summary>USB-IF 把 0x05AC 分配给 Apple；GANSS 接收器沿用该号，不是笔误。</summary>
    public const ushort VendorId = 0x05AC;
    public const ushort ProductId = 0x024F;
    public const int PayloadLength = 32;
    public const int HidReportLength = 33;

    public static bool IsSupportedInterface(ushort vendorId, ushort productId, string path, string product,
        ushort usagePage, ushort usage, ushort inputLength, ushort outputLength, byte? reportId)
        => vendorId == VendorId && productId == ProductId
           && usagePage == 0xFF60 && usage == 0x0061
           && inputLength == HidReportLength && outputLength == HidReportLength
           && reportId == 0
           && (IsCapturedInterfaceIndex(path) || LooksLikeGanss(product) || LooksLikeAulaF108(product));

    /// <summary>贡献者机器上电量集合固定在 mi_03；换固件或批次可能变，所以只当证据之一，不当唯一依据。</summary>
    public static bool IsCapturedInterfaceIndex(string path)
        => path.Contains("&mi_03#", StringComparison.OrdinalIgnoreCase);

    /// <summary>狼蛛 F108 Pro 自报的产品名含这个串（本机实测 <c>F108Pro Dongle</c>）。</summary>
    public const string AulaProductHint = "F108";

    /// <summary>接收器自报的产品名含品牌名时，接口编号漂移也认领（产品名是设备自己声明的身份）。</summary>
    public static bool LooksLikeGanss(string product)
        => product.Contains("GANSS", StringComparison.OrdinalIgnoreCase)
           || product.Contains("GS3104", StringComparison.OrdinalIgnoreCase);

    /// <summary>狼蛛 F108 Pro 与 GS3104T-Pro 在 HID 层完全同指纹，品牌识别只剩产品名这一条路。</summary>
    public static bool LooksLikeAulaF108(string product)
        => product.Contains(AulaProductHint, StringComparison.OrdinalIgnoreCase);

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
