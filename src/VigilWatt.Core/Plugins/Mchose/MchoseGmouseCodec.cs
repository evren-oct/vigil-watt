namespace VigilWatt.Core.Plugins.Mchose;

/// <summary>
/// 功 能： 迈从 Gmouse 主动查询协议（G3 V2 等）的纯帧编解码，不碰句柄、不写日志。
/// 2026/10/20 Sunshine
/// 协议逆向自官方 M HUB 网页端 /gmouse 与社区 aliezzzz/mchose-battery；细节见 docs/hardware/mchose.md §4。
/// 与 K7（0x4D 帧 / 0x0900 命令）是两条完全不同的链路：Gmouse 是 65 字节 <c>00 55 30 A5…</c> 查询帧，
/// 应答前缀 <c>AA 30</c>，靠 usagePage 0xFF01 / usage 0x10 的完整指纹与 V2 世代的被动 E2 链路区分。
/// </summary>
public static class MchoseGmouseCodec
{
    /// <summary>厂 ID（迈从新世代）。</summary>
    public const ushort VendorId = 0x3837;

    /// <summary>Gmouse 协议族接收器 PID（G3 V2 = 0x4245；同族还有 0xA8A5 / 0xA8A4）。</summary>
    public const ushort PidG3V2 = 0x4245;

    /// <summary>命令通道的 usagePage / usage（USB 路径里是 <c>mi_02</c>，无 COL 后缀）。</summary>
    public const ushort UsagePage = 0xFF01;
    public const ushort Usage = 0x0010;

    /// <summary>帧总长（65 = 64 数据 + 1 report ID）。</summary>
    public const int FrameLength = 65;

    /// <summary>Windows 的 ReadInput 缓冲可能带/不带 report ID 前缀，两种偏移都试（与其它解析器一致）。</summary>
    private static readonly int[] Offsets = { 0, 1 };

    /// <summary>
    /// 接口是不是 Gmouse 的命令通道。判据是**完整指纹**（厂 ID + PID + 厂商用法 + 双方向的 65 字节），
    /// 不做"同 VID 就试试"的推断：迈从同厂 ID 下还有 20 字节的键盘会话通道、64 字节的 K7 命令通道
    /// 和 ≥60 字节的 V2 鼠标被动 E2 通道，判错会把 Gmouse 查询帧发到别的设备上。
    /// </summary>
    public static bool IsSupportedInterface(ushort vendorId, ushort productId,
        ushort usagePage, ushort usage, ushort inputLength, ushort outputLength)
        => vendorId == VendorId
           && productId == PidG3V2
           && usagePage == UsagePage
           && usage == Usage
           && inputLength >= FrameLength
           && outputLength >= FrameLength;

    /// <summary>构造 Gmouse 电量查询帧（65 字节）：[0]=0（report ID），[1..8]=<c>55 30 A5 0B 2E 01 01 01</c>，余零。</summary>
    public static byte[] CreateQuery()
    {
        var report = new byte[FrameLength];
        report[0] = 0x00;
        report[1] = 0x55;
        report[2] = 0x30;
        report[3] = 0xA5;
        report[4] = 0x0B;
        report[5] = 0x2E;
        report[6] = 0x01;
        report[7] = 0x01;
        report[8] = 0x01;
        return report;
    }

    /// <summary>
    /// 解析 Gmouse 电量应答。应答前缀 <c>AA 30</c>（可能带 report ID 前缀，偏移 0 或 1）；
    /// 电量在 <c>[offset+8]</c>、充电标志在 <c>[offset+9]</c>（0=未充电，非 0=充电中）。
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> report, out int percent, out bool charging)
    {
        percent = 0;
        charging = false;

        foreach (int off in Offsets)
        {
            if (report.Length < off + 10) continue;
            if (report[off] != 0xAA || report[off + 1] != 0x30) continue;

            int value = report[off + 8];
            if (!IsPlausiblePercent(value)) return false;
            percent = value;
            charging = report[off + 9] != 0;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 0 是"未就绪/占位"（dongle 没连上设备时的常见值），**不能**当 1% 报出去；
    /// 1..100 一律接受——鼠标真能到 1%，拒掉它就会漏掉低电量提醒。
    /// </summary>
    private static bool IsPlausiblePercent(int percent) => percent is >= 1 and <= 100;
}
