namespace VigilWatt.Core.Plugins.Cherry;

/// <summary>
/// 功 能： CHERRY 无线键盘 64 字节厂商帧（report 0x04）的纯编解码，不碰句柄、不写日志，便于单测。
/// 2026/10/18 17:24:10 sheng
/// 帧布局与块表见 docs/hardware/brands/cherry.md §1。
/// </summary>
internal static class CherryCodec
{
    /// <summary>厂商集合 mi_01&amp;col04（usage 0xFF1C）的 report ID，已由本机 caps 与官方软件抓包双向确认。</summary>
    public const byte ReportId = 0x04;

    public const int FrameLength = 64;

    /// <summary>应答 payload 的起始下标：帧头 8 字节（ID/令牌/块/长度/偏移/保留）。</summary>
    public const int PayloadOffset = 8;

    /// <summary>设备信息块，payload 首两字节是 0xAA 0x55 签名，隔两个零字节后才是 VID/PID（真机布局）。</summary>
    public const byte BlockDeviceInfo = 0x1D;

    /// <summary>电量块，payload[0] = 百分比 0..100（与官方软件同源）。</summary>
    public const byte BlockBattery = 0x1A;

    public const byte SignatureLow = 0xAA;
    public const byte SignatureHigh = 0x55;

    /// <summary>
    /// 构造读块请求。**令牌恒为 0**：官方软件抓包与实机应答都只验证过 0，
    /// 未验证的令牌值不引入（docs/hardware/safety.md §3 规则 1）。
    /// </summary>
    public static byte[] BuildRequest(byte block, byte length, ushort offset = 0)
    {
        var frame = new byte[FrameLength];
        frame[0] = ReportId;
        frame[3] = block;
        frame[4] = length;
        frame[5] = (byte)(offset & 0xFF);
        frame[6] = (byte)(offset >> 8);
        return frame;
    }

    /// <summary>解析应答：核对 report ID、块号与长度，payload 从 <see cref="PayloadOffset"/> 起。</summary>
    public static bool TryParse(byte[] frame, byte expectedBlock, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        if (frame.Length < PayloadOffset) return false;
        if (frame[0] != ReportId) return false;
        if (frame[3] != expectedBlock) return false;

        int length = frame[4];
        if (length == 0) return false;
        int available = frame.Length - PayloadOffset;
        if (length > available) length = (byte)available;

        payload = new byte[length];
        Array.Copy(frame, PayloadOffset, payload, 0, length);
        return true;
    }

    /// <summary>设备信息块校验：签名 + VID 必须都对，用来确认这条通道真是这把键盘而不是同代的其他型号。</summary>
    public static bool TryParseDeviceInfo(byte[] payload, ushort expectedVid, out ushort pid)
    {
        pid = 0;
        if (payload.Length < 8) return false;
        if (payload[0] != SignatureLow || payload[1] != SignatureHigh) return false;
        ushort vid = (ushort)(payload[4] | (payload[5] << 8));
        if (vid != expectedVid) return false;
        pid = (ushort)(payload[6] | (payload[7] << 8));
        return true;
    }

    /// <summary>
    /// 电量块：payload[0] 即百分比，payload[1] 最低位是充电位（插线时官方软件画闪电，本机 48/0 → 51/1 实测）。
    /// 100 以上或空包视为无效（设备休眠瞬态）。
    /// </summary>
    public static bool TryParseBattery(byte[] payload, out int percent, out bool charging)
    {
        percent = 0;
        charging = false;
        if (payload.Length < 1) return false;
        if (payload[0] > 100) return false;
        percent = payload[0];
        charging = payload.Length > 1 && (payload[1] & 0x01) != 0;
        return true;
    }
}
