namespace VigilWatt.Core.Plugins.Atk;

/// <summary>
/// 功 能： ATK 键盘走标准 VIA 协议读电量的报文构造与解析。
/// 2026/10/20 10:20:00 MOSS
/// 协议与验证范围见 docs/hardware/atk.md §3。
/// </summary>
public static class AtkViaCodec
{
    /// <summary>VIA 命令：读键盘值（get_keyboard_value）。</summary>
    public const byte CommandGetValue = 0x02;

    /// <summary>ATK 在 VIA 之上加的自定义通道 ID。</summary>
    public const byte CustomId = 0x96;

    /// <summary>自定义通道里的电量键。</summary>
    public const byte KeyBattery = 0x24;

    /// <summary>键盘电量通道的 usage page。</summary>
    public const ushort UsagePage = 0xFF60;

    /// <summary>该通道的 report ID。</summary>
    public const byte ReportId = 0x00;

    /// <summary>响应里电量、充电标志相对帧首的偏移（帧首 = 命令字节，不含 report ID）。</summary>
    private const int PercentOffset = 3;

    private const int ChargeOffset = 4;

    /// <summary>键盘电量集合判据：只认 33 字节那条 VIA 通道。</summary>
    public static bool IsSupportedInterface(ushort vendorId, ushort usagePage,
        ushort inputLength, ushort outputLength, byte? reportId)
        => vendorId == AtkCodec.VendorId && usagePage == UsagePage
           && inputLength == outputLength && outputLength >= 5
           && (reportId is null or ReportId);

    /// <summary>查询帧：<c>[report ID][命令][自定义通道][电量键]</c>，余下补零。</summary>
    public static byte[] CreateQuery(int reportLength)
    {
        var report = new byte[Math.Max(reportLength, 5)];
        report[0] = ReportId;
        report[1] = CommandGetValue;
        report[2] = CustomId;
        report[3] = KeyBattery;
        return report;
    }

    /// <summary>
    /// 解析响应（1–100，0 是休眠占位），两种偏移都试。充电标志非 0 = 充电中；
    /// 官方客户端充电时不显示百分比，这里照读数显示并标充电中。
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> report, out int percent, out bool charging)
    {
        percent = 0;
        charging = false;
        for (int off = 1; off >= 0; off--)
        {
            if (report.Length < off + ChargeOffset + 1) continue;
            var frame = report[off..];
            if (frame[0] != CommandGetValue || frame[1] != CustomId || frame[2] != KeyBattery) continue;
            if (frame[PercentOffset] is 0 or > 100) continue;
            percent = frame[PercentOffset];
            charging = frame[ChargeOffset] != 0;
            return true;
        }
        return false;
    }
}
