namespace VigilWatt.Core.Plugins.Mchose;

/// <summary>
/// 功 能： 迈从 Gmouse 主动查询协议的纯帧编解码，不碰句柄、不写日志。
/// 2026/10/20 Sunshine
/// 协议世代与 K7（0x4D/0x0900）无关；接口指纹、帧格式与未验证边界见 docs/hardware/mchose.md §4。
/// </summary>
public static class MchoseGmouseCodec
{
    /// <summary>厂 ID（迈从新世代）。</summary>
    public const ushort VendorId = 0x3837;

    /// <summary>G3 V2 接收器 PID。</summary>
    public const ushort PidG3V2 = 0x4245;

    /// <summary>同协议的另一只接收器（蝰蛇 K-snake X11）。来源项目早先把 0xA8A5 当 VID 记下，本机实测确为 VID。</summary>
    public const ushort VidKsnake = 0xA8A5;
    public const ushort PidKsnake = 0x2255;

    /// <summary>命令通道的 usagePage / usage（USB 路径里是 <c>mi_02</c>，无 COL 后缀）。</summary>
    public const ushort UsagePage = 0xFF01;
    public const ushort Usage = 0x0010;

    /// <summary>帧总长（65 = 64 数据 + 1 report ID）。</summary>
    public const int FrameLength = 65;

    /// <summary>同 PID 的其它厂商接口（如 0xFF06/0x0002，64/64）：它们**没有输出通道**，必须排除，
    /// 否则会落进 MouseV2 兜底、每 15 秒被写一条无意义的触发帧（mchose.md §4.6）。</summary>
    public static bool IsSiblingVendorInterface(ushort vendorId, ushort productId)
        => vendorId == VidKsnake && productId == PidKsnake;

    /// <summary>接收器产品串。兄弟型号可能共用 VID/PID，故新 VID 还要求产品串含它才认（宁可漏认不误认）。</summary>
    public const string ReceiverProductMarker = "Receiver";

    /// <summary>Windows 的 ReadInput 缓冲可能带/不带 report ID 前缀，两种偏移都试（与其它解析器一致）。</summary>
    private static readonly int[] Offsets = { 0, 1 };

    /// <summary>接口是不是 Gmouse 命令通道：完整指纹（VID+PID+厂商用法+双向 65 字节），判错会把帧发到别的设备上。
    /// 0x3837 世代带产品串收窄会让已验证的 G3 V2 一起失效，故只有 0xA8A5 世代才查 <paramref name="product"/>。</summary>
    public static bool IsSupportedInterface(ushort vendorId, ushort productId, string product,
        ushort usagePage, ushort usage, ushort inputReportLength, ushort outputReportLength)
    {
        if (usagePage != UsagePage || usage != Usage) return false;
        if (inputReportLength < FrameLength || outputReportLength < FrameLength) return false;

        if (vendorId == VendorId && productId == PidG3V2) return true;
        return vendorId == VidKsnake && productId == PidKsnake
            && product.Contains(ReceiverProductMarker, StringComparison.OrdinalIgnoreCase);
    }

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

    /// <summary>解析电量应答：前缀 <c>AA 30</c>（偏移 0 或 1），电量 <c>[off+8]</c>、充电 <c>[off+9]</c>（非 0 即充电中）。</summary>
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

    /// <summary>0 是"未就绪"占位（推断，见 mchose.md §4.5）；1..100 一律接受——鼠标真能到 1%，拒掉会漏掉低电量提醒。</summary>
    private static bool IsPlausiblePercent(int percent) => percent is >= 1 and <= 100;
}
