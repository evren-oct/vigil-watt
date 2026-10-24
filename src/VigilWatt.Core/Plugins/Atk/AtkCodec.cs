namespace VigilWatt.Core.Plugins.Atk;

/// <summary>
/// 功 能： ATK 鼠标 16 字节 op 帧的构造与解析。
/// 2026/10/20 10:20:00 MOSS
/// 协议与验证范围见 docs/hardware/atk.md §2。
/// </summary>
public static class AtkCodec
{
    /// <summary>接收器厂 ID（鼠标与键盘同号段，按 usage page 分流）。</summary>
    public const ushort VendorId = 0x373B;

    /// <summary>鼠标电量通道的 usage page。</summary>
    public const ushort MouseUsagePage = 0xFF02;

    /// <summary>鼠标通道的 report ID。线上长度含 report ID，共 17 字节。</summary>
    public const byte MouseReportId = 0x08;

    /// <summary>进/出报告长度（含 report ID）。</summary>
    public const int MouseReportLength = 17;

    /// <summary>op 帧长度，末字节是校验和。</summary>
    public const int FrameSize = 16;

    /// <summary>电量 OpCode；响应 data0 是百分比。</summary>
    public const byte OpBattery = 0x04;

    /// <summary>校验和基数：chk = (0x4D - sum(前 15 字节)) &amp; 0xFF。</summary>
    public const byte ChecksumBase = 0x4D;

    /// <summary>
    /// 鼠标电量集合判据。同接收器上还有 0xFF03/0xFF05/0xFF06 等集合，
    /// 全部不认——往它们写 op 帧没有意义。
    /// </summary>
    public static bool IsSupportedInterface(ushort vendorId, ushort usagePage,
        ushort inputLength, ushort outputLength, byte? reportId)
        => vendorId == VendorId && usagePage == MouseUsagePage
           && inputLength == MouseReportLength && outputLength == MouseReportLength
           && reportId == MouseReportId;

    /// <summary>17 字节查询帧：report ID + <c>04 00 … 00</c> + 校验和。</summary>
    public static byte[] CreateQuery()
    {
        var frame = new byte[FrameSize];
        frame[0] = OpBattery;
        frame[FrameSize - 1] = Checksum(frame);
        return BuildReport(MouseReportId, frame, MouseReportLength);
    }

    /// <summary>把 op 帧套上 report ID 并补零到 <paramref name="length"/>。</summary>
    public static byte[] BuildReport(byte reportId, byte[] frame, int length)
    {
        var report = new byte[Math.Max(length, frame.Length + 1)];
        report[0] = reportId;
        Buffer.BlockCopy(frame, 0, report, 1, frame.Length);
        return report;
    }

    /// <summary>校验和只累加前 <see cref="FrameSize"/> - 1 字节。</summary>
    public static byte Checksum(ReadOnlySpan<byte> frame)
    {
        int sum = 0;
        for (int i = 0; i < FrameSize - 1 && i < frame.Length; i++) sum += frame[i];
        return (byte)((ChecksumBase - (sum & 0xFF)) & 0xFF);
    }

    /// <summary>
    /// 解析响应（1–100，0 是休眠占位），两种 report ID 偏移都试。**必须校验和通过**：
    /// 键盘通道会把请求原样回显且 data0 与电量无关，回显帧的校验和是旧值，这是唯一挡得住它的判据。
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> report, out int percent)
    {
        percent = 0;
        for (int off = 0; off <= 1; off++)
        {
            if (report.Length < off + FrameSize) continue;
            var frame = report.Slice(off, FrameSize);
            if (frame[0] != OpBattery) continue;
            if (frame[FrameSize - 1] != Checksum(frame)) continue;
            if (frame[5] is 0 or > 100) continue;
            percent = frame[5];
            return true;
        }
        return false;
    }
}
