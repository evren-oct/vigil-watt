using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Darkrim;

/// <summary>
/// 功 能： 寸渊影弑 (DARKRIM) 杰理 dongle 的帧编解码——纯函数，不碰句柄，便于单测。
/// 2026/10/19 19:40:00 zc165
/// 帧结构与 CRC 依据见 docs/hardware/brands/darkrim.md §1。
/// </summary>
internal static class DarkrimCodec
{
    /// <summary>帧头魔数：每帧第 0 字节恒为 0xFE。</summary>
    public const byte Magic = 0xFE;

    /// <summary>帧固定头长度：[0]魔数 [1]seq [2]cmd [3]方向 [4]来源 [5]块号 [6]块数 [7]载荷长 [8]块标志。</summary>
    public const int HeaderLength = 9;

    /// <summary>帧尾 CRC 长度（CRC-16 大端）。</summary>
    public const int CrcLength = 2;

    /// <summary>命令字：dongle 主动上报（0x11 家族）。</summary>
    public const byte CmdNotify = 0x11;

    /// <summary>载荷前缀长度：子类型 + 剩余长度。</summary>
    public const int PayloadPrefix = 2;

    /// <summary>通知子类型：电量。</summary>
    public const byte NotifyBattery = 0x01;

    /// <summary>通知子类型：设备名/固件。</summary>
    public const byte NotifyIdentity = 0x03;

    /// <summary>通知子类型：dongle↔耳机链路。</summary>
    public const byte NotifyLink = 0x04;

    /// <summary>CRC-16/CCITT-FALSE：poly 0x1021、init 0xFFFF、不反射、不异或输出。</summary>
    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }

    /// <summary>校验一帧的尾部 CRC（覆盖魔数到载荷末尾，大端存放）。</summary>
    public static bool IsCrcValid(ReadOnlySpan<byte> frame)
    {
        if (!TryReadHeader(frame, out var h)) return false;
        int crcAt = h.BaseOffset + HeaderLength + h.PayloadLength;
        if (crcAt + CrcLength > frame.Length) return false;

        ushort want = (ushort)((frame[crcAt] << 8) | frame[crcAt + 1]);
        return Crc16(frame[h.BaseOffset..crcAt]) == want;
    }

    /// <summary>一帧解析出来的头部字段。<see cref="BaseOffset"/> = 该帧在缓冲区里的起始下标。</summary>
    public readonly record struct Frame(byte Seq, byte Cmd, int PayloadLength, int PayloadOffset, int BaseOffset);

    /// <summary>
    /// 定位并解析帧头，自动兼容"带 report ID 前缀"的缓冲区。
    ///
    /// **为什么必须兼容**：同一份数据，hidapi（Python 探针）会把 report ID 剥掉，
    /// 而本应用走 FileStream 裸读，Windows 会把 report ID 原样放在首字节。
    /// 实测 MI_03 的 report ID 是 0x00，于是本应用读到的是 <c>00 FE 00 11 …</c>，
    /// 魔数在 <c>[1]</c> 而不是 <c>[0]</c>——只认 <c>[0]</c> 会让所有帧都解析失败。
    /// </summary>
    public static bool TryReadHeader(ReadOnlySpan<byte> frame, out Frame header)
    {
        header = default;

        // 两种布局都试：下标 0 直接是魔数（无前缀），或下标 1 是魔数（前面有一个 report ID 字节）。
        foreach (int b in stackalloc int[] { 0, 1 })
        {
            if (frame.Length < b + HeaderLength + CrcLength) continue;
            if (frame[b] != Magic) continue;

            int payloadLen = frame[b + 7];
            if (b + HeaderLength + payloadLen + CrcLength > frame.Length) continue;

            header = new Frame(frame[b + 1], frame[b + 2], payloadLen, b + HeaderLength, b);
            return true;
        }
        return false;
    }

    /// <summary>主机→dongle 的命令字（抓包里所有请求都是 0x10）。</summary>
    public const byte CmdRequest = 0x10;

    /// <summary>请求来源标识（抓包里固定 0x02）。</summary>
    private const byte RequestSource = 0x02;

    /// <summary>GetBasicInfo 的 TLV 类型（官方日志 <c>GetBasicInfo(TLV0x09)</c>）。</summary>
    public const byte TlvGetBasicInfo = 0x09;

    /// <summary>
    /// 构造一条 GetBasicInfo 查询帧——**就是官方客户端每次连上设备时发的同一条只读命令**。
    ///
    /// 真机验证（2026-10-19）：发 <c>fe 00 10 00 02 00 00 03 00 09 01 00 e2 3c</c> 后，
    /// dongle 立即回 <c>0x11/01</c> 电量（<c>5a</c> = 90%，与官方界面一致）+ EQ + 名称/固件。
    /// 构造器用官方抓到的 GetVersion 帧做过逐字节自检（见单测）。
    /// </summary>
    public static byte[] BuildGetBasicInfoRequest(byte seq = 0x00)
    {
        ReadOnlySpan<byte> payload = [TlvGetBasicInfo, 0x01, 0x00];
        return BuildFrame(seq, payload, CmdRequest, RequestSource);
    }

    /// <summary>按实测格式拼一帧（含尾部 CRC-16 大端）。</summary>
    public static byte[] BuildFrame(byte seq, ReadOnlySpan<byte> payload, byte cmd, byte source)
    {
        var frame = new byte[HeaderLength + payload.Length + CrcLength];
        frame[0] = Magic;
        frame[1] = seq;
        frame[2] = cmd;
        frame[3] = 0x00;              // 方向
        frame[4] = source;
        frame[5] = 0x00;              // 块号
        frame[6] = 0x00;              // 块数（实测单块请求恒为 0）
        frame[7] = (byte)payload.Length;
        frame[8] = 0x00;              // 块标志
        payload.CopyTo(frame.AsSpan(HeaderLength));

        ushort crc = Crc16(frame.AsSpan(0, HeaderLength + payload.Length));
        frame[^2] = (byte)(crc >> 8);
        frame[^1] = (byte)(crc & 0xFF);
        return frame;
    }

    /// <summary>
    /// 判断一帧是不是"自己刚发出去的请求被回显"。
    ///
    /// 实测 MI_03 接口会把写入原样读回。它不是设备应答，解析时必须跳过——
    /// 否则会把请求当成通知处理，而且永远等不到真正的电量应答。
    /// 判据只有命令字方向：<c>0x10</c> = 主机→dongle（请求/回显），<c>0x11</c> = dongle 上报。
    /// </summary>
    public static bool IsEchoOfRequest(ReadOnlySpan<byte> frame)
        => TryReadHeader(frame, out var h) && h.Cmd == CmdRequest;

    /// <summary>三路电量读数（百分比）；该通道无读数时对应项为 null。</summary>
    /// <param name="Left">左耳。</param>
    /// <param name="Right">右耳。</param>
    /// <param name="Case">充电盒。</param>
    /// <param name="Single">单耳/单电池形态：只有一路读数。</param>
    /// <param name="Charging">任一路带充电位即为 true。</param>
    public readonly record struct BatteryReport(int? Left, int? Right, int? Case, int? Single, bool Charging)
    {
        public bool HasAny => Left.HasValue || Right.HasValue || Case.HasValue || Single.HasValue;
    }

    /// <summary>电量字节里的充电标志位（真机校准：放电 0x5A、充电 0xDA）。</summary>
    public const byte ChargeBit = 0x80;

    /// <summary>
    /// 解析电量通知帧（cmd=0x11、载荷[0]=0x01）。
    ///
    /// 载荷布局：<c>子类型(0x01) | 剩余长度 | 数据…</c>，数据字节的编码是
    /// **bit7 = 充电中，低 7 位 = 百分比**。
    ///
    /// **真机校准（2026-10-19，同一 90% 电量下插拔充电线对照）**：
    /// <list type="bullet">
    /// <item>放电：<c>fe 00 11 00 03 00 00 03 00 01 01 5A 98 B4</c> → <c>0x5A</c> = 90%</item>
    /// <item>充电：<c>… 01 01 DA …</c> → <c>0xDA</c> = <c>0x80 | 0x5A</c> = 充电中 90%</item>
    /// </list>
    /// 官方界面此刻显示"充电中"，与此位严格对应。
    /// </summary>
    public static bool TryParseBattery(ReadOnlySpan<byte> frame, out BatteryReport report)
    {
        report = default;
        if (!TryReadHeader(frame, out var h)) return false;
        if (h.Cmd != CmdNotify || h.PayloadLength < PayloadPrefix) return false;

        var payload = frame.Slice(h.PayloadOffset, h.PayloadLength);
        if (payload[0] != NotifyBattery) return false;

        int dataLen = Math.Min(payload[1], payload.Length - PayloadPrefix);
        if (dataLen <= 0) return false;

        static int? Percent(byte raw)
        {
            int pct = raw & ~ChargeBit;
            return pct is >= 1 and <= 100 ? pct : null;
        }

        static bool IsCharging(byte raw) => (raw & ChargeBit) != 0;

        if (dataLen == 1)
        {
            byte one = payload[PayloadPrefix];
            var single = Percent(one);
            if (!single.HasValue) return false;
            report = new BatteryReport(null, null, null, single, IsCharging(one));
            return true;
        }

        byte left = dataLen >= 1 ? payload[PayloadPrefix] : (byte)0;
        byte right = dataLen >= 2 ? payload[PayloadPrefix + 1] : (byte)0;
        byte caseSlot = dataLen >= 3 ? payload[PayloadPrefix + 2] : (byte)0;

        report = new BatteryReport(
            Percent(left),
            Percent(right),
            Percent(caseSlot),
            null,
            IsCharging(left) || IsCharging(right) || IsCharging(caseSlot));

        // 帧本身是合法的电量通知：三路全 0 = 耳机没连上，交给调用方决定怎么处理，
        // 不要在这里返回 false——那会让"耳机断连"与"这不是电量帧"混成同一种结果。
        return true;
    }

    /// <summary>
    /// 解析链路通知帧（cmd=0x11、载荷[0]=0x04）：数据首字节非 0 = 耳机已连上。
    /// 这是"耳机在不在"的零歧义证据，可用于快速判离线。
    ///
    /// ⚠ 真机实测（2026-10-19）：耳机空闲时该位会在 1↔0 之间反复跳（45 秒内 3 个来回），
    /// 属于"空闲休眠/唤醒"，**不能**据此直接判离线，只能走 <see cref="MarkOfflineCandidate"/>
    /// 那套延迟确认（与雷柏 RF 位同理）。
    /// </summary>
    public static bool TryParseLink(ReadOnlySpan<byte> frame, out bool linked)
    {
        linked = false;
        if (!TryReadHeader(frame, out var h)) return false;
        if (h.Cmd != CmdNotify || h.PayloadLength < PayloadPrefix + 1) return false;

        var payload = frame.Slice(h.PayloadOffset, h.PayloadLength);
        if (payload[0] != NotifyLink) return false;

        linked = payload[PayloadPrefix] != 0;
        return true;
    }

    /// <summary>
    /// 把多路读数收敛成"设备电量"。
    /// 双耳取**较低的那只**（先耗尽的决定还能用多久）；只有充电盒读数时用充电盒。
    /// </summary>
    public static int? Combine(BatteryReport report)
    {
        if (report.Single.HasValue) return report.Single;
        var ears = new[] { report.Left, report.Right }.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        if (ears.Length > 0) return ears.Min();
        return report.Case;
    }

    /// <summary>充电标志只有"是否充电"一档：收尾规则（未充电且 100% ⇒ 已充满）见 <see cref="ChargingStateMap"/>。</summary>
    public static ChargingState MapCharging(int? percent, bool charging)
        => ChargingStateMap.FromChargingFlag(charging, percent ?? 0);
}
