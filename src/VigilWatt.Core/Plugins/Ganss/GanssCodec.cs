namespace VigilWatt.Core.Plugins.Ganss;

/// <summary>
/// 功 能： 0x05AC:024F + FF60/0x0061 + 33 字节这一族键盘（GANSS GS3104T-Pro、AULA F108 Pro）
/// 的电量报文构造与解析。**纯帧编解码**，零 I/O。
/// 2026/10/19 19:45:00 xliu（GS3104T-Pro 首版，贡献者 luze258 抓包）
/// 2026/10/21 扩展：AULA F108 Pro 经本机真机复核为同一硅方案（判据、帧、校验和全部一致），
///                   只是品牌不同，故不另开插件，改由产品名区分显示名。见 docs/hardware/protocols.md §14。
///
/// 协议：32 字节 payload（HID 缓冲区 33 字节，report ID 恒 0），末字节 = 前 32 字节求和取低字节。
/// 查询帧固定 <c>00 20 01 00 … 21</c>；应答 <c>20 01 00 xx …</c> 的 <c>[3]</c> 就是电量。
/// </summary>
public static class GanssCodec
{
    /// <summary>USB-IF 把 0x05AC 分配给 Apple；接收器沿用该号，不是笔误。</summary>
    public const ushort VendorId = 0x05AC;
    public const ushort ProductId = 0x024F;
    public const int PayloadLength = 32;
    public const int HidReportLength = 33;

    /// <summary>产品名里出现这个串就按狼蛛 F108 Pro 处理（本机实测自报 <c>F108Pro Dongle</c>）。</summary>
    public const string AulaProductHint = "F108";

    public static bool IsSupportedInterface(ushort vendorId, ushort productId, string path, string product,
        ushort usagePage, ushort usage, ushort inputLength, ushort outputLength, byte? reportId)
        => vendorId == VendorId && productId == ProductId
           && usagePage == 0xFF60 && usage == 0x0061
           && inputLength == HidReportLength && outputLength == HidReportLength
           && reportId == 0
           && (IsCapturedInterfaceIndex(path) || LooksLikeGanss(product) || LooksLikeAulaF108(product));

    /// <summary>贡献者机器上电量集合固定在 mi_03；本机 AULA F108 Pro 也是 mi_03（同一硅方案）。</summary>
    public static bool IsCapturedInterfaceIndex(string path)
        => path.Contains("&mi_03#", StringComparison.OrdinalIgnoreCase);

    /// <summary>接收器自报的产品名含品牌名时，接口编号漂移也认领（产品名是设备自己声明的身份）。</summary>
    public static bool LooksLikeGanss(string product)
        => product.Contains("GANSS", StringComparison.OrdinalIgnoreCase)
           || product.Contains("GS3104", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 狼蛛 F108 Pro 的接收器自报 <c>F108Pro Dongle</c>（厂商串为空）。
    /// 同 VID/PID/usage/长度/mi 号下无法再靠接口区分，所以品牌识别只剩产品名这一条路。
    /// </summary>
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
