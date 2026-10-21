namespace VigilWatt.Core.Plugins.Mchose;

/// <summary>
/// <c>0x0600</c> 状态事件的解析结果。设备**一次只报一个字段**（电量帧与充电帧是两条不同的帧），
/// 故两项各自可空，调用方缺哪个就沿用上一次的值。
/// </summary>
public readonly record struct K7StatusEvent(int? Percent, byte? ChargeStatus);

/// <summary>
/// 功 能： 迈从 K7 V2 Ultra+（接收器 0x3837:0x1018）的纯帧编解码，不碰句柄、不写日志。
/// 2026/10/21 Mightling
/// 帧与字段表取自官方 M HUB 打包 JS：帧构造 <c>i1e()</c>、设备信息解析 <c>_K()</c>、事件解析 <c>LA()</c>。
/// 这颗 dongle 只有一条可用通道：usage <c>0xFF01/0x0001</c> 的 64 字节 output（report ID <c>0x4D</c>）——
/// feature 通道"写通读不通"（<c>SetFeature</c> 成功、<c>GetFeature</c> 恒 <c>ERROR_GEN_FAILURE</c>），
/// 所以 A5 / A7 / gmouse / V2 世代那几族（都靠 feature 读或靠 E2 状态帧）在这颗 dongle 上都不成立。
/// 细节见 docs/hardware/protocols.md §13。
/// </summary>
public static class MchoseK7Codec
{
    /// <summary>厂 ID（迈从新世代）。</summary>
    public const ushort VendorId = 0x3837;

    /// <summary>K7 V2 Ultra+ 的 2.4G 接收器 PID（M HUB 里 productKey 尾部是 <c>4120</c> = 0x1018）。</summary>
    public const ushort PidK7 = 0x1018;

    /// <summary>命令通道的 usagePage / usage（USB 路径里是 <c>mi_02&amp;col02</c>）。</summary>
    public const ushort UsagePage = 0xFF01;
    public const ushort Usage = 0x0001;

    /// <summary>命令通道的 report ID（也是帧首字节）。</summary>
    public const byte ReportId = 0x4D;

    /// <summary>帧的 version 字节，恒 <c>0x01</c>。</summary>
    public const byte Version = 0x01;

    /// <summary>请求帧的 flags。**必须是 1**：官方 <c>i1e</c> 只在 <c>flags == 1</c> 时才写校验字节。</summary>
    public const byte FlagsRequest = 0x01;

    /// <summary>帧总长（HID 报告含 report ID 首字节）。</summary>
    public const int FrameLength = 64;

    /// <summary>命令：读设备信息（应答里带连接状态、充电状态、电量、游戏模式）。</summary>
    public const ushort CmdDeviceInfo = 0x0900;

    /// <summary>命令：电源/状态事件（设备**主动**上报，电量与充电状态都走这一条）。</summary>
    public const ushort CmdStatusEvent = 0x0600;

    /// <summary>设备信息应答的最小 payload 长度（要读到 <c>payload[13]</c>）。</summary>
    private const int DeviceInfoPayloadLength = 14;

    /// <summary>状态事件的最小 payload 长度（充电状态在 <c>payload[12]</c>）。</summary>
    private const int StatusEventPayloadLength = 13;

    /// <summary>业务码字节，K7 恒 0。</summary>
    private const byte BizCode = 0x00;

    /// <summary>Windows 的 ReadInput 缓冲可能带/不带 report ID 前缀，两种偏移都试（与 A5/A7 解析器一致）。</summary>
    private static readonly int[] Offsets = { 0, 1 };

    /// <summary>
    /// 接口是不是 K7 的命令通道。判据是**完整指纹**（厂 ID + PID + 厂商用法 + 两个方向的 64 字节），
    /// 不做"同 VID 就试试"的推断：迈从同厂 ID 下还有 20 字节的键盘会话通道和 65 字节的 V2 鼠标通道，
    /// 判错会把 0x4D 帧发到别的设备上。
    /// </summary>
    public static bool IsSupportedInterface(ushort vendorId, ushort productId,
        ushort usagePage, ushort usage, ushort inputLength, ushort outputLength)
        => vendorId == VendorId
           && productId == PidK7
           && usagePage == UsagePage
           && usage == Usage
           && inputLength >= FrameLength
           && outputLength >= FrameLength;

    /// <summary>读设备信息（电量 / 充电状态 / 连接状态）的请求帧。</summary>
    public static byte[] BuildDeviceInfoQuery() => BuildFrame(CmdDeviceInfo);

    /// <summary>
    /// 构造 K7 帧：<c>4D 01 flags len cmdLo cmdHi biz seq payload… chk</c>，补零到 64 字节。
    /// <b>chk = XOR(frame[2 .. 7+len])</b>，且**只有 <c>flags == 1</c> 才计算**——这是官方 <c>i1e</c> 的原样行为。
    /// 早前按"求和 + flags=0x03"写过几十轮，设备一律把自己的写原样回显、什么都不做，
    /// 因此被误判成"这颗 dongle 只会回显、没有查询通道"。校验是异或，不是求和。
    /// </summary>
    public static byte[] BuildFrame(ushort command, params byte[] payload)
    {
        var frame = new byte[FrameLength];
        frame[0] = ReportId;
        frame[1] = Version;
        frame[2] = FlagsRequest;
        frame[3] = (byte)payload.Length;
        frame[4] = (byte)(command & 0xFF);
        frame[5] = (byte)(command >> 8);
        frame[6] = BizCode;
        frame[7] = 0x00;
        Array.Copy(payload, 0, frame, 8, payload.Length);

        byte xor = 0;
        for (int i = 2; i < 8 + payload.Length; i++) xor ^= frame[i];
        frame[8 + payload.Length] = xor;
        return frame;
    }

    /// <summary>
    /// 这一帧是不是"设备说话了"（只要帧头是 <c>4D 01</c> 且声明长度装得下就算）。
    ///
    /// 判"有没有应答"必须用它，而**不能**用解析是否成功：鼠标在睡时 dongle 只回一条
    /// <c>4D 01 01 01 00 09 00 00 FF</c>（payload 只有 1 字节、内容是 <c>0xFF</c>），
    /// 官方 <c>hgt()</c> 对它的处理就是重试 + 容忍无读数。把它当"没应答"记账的话，
    /// 睡着的鼠标连轮三轮就被判离线并熔断停写，醒了也不会再被问。
    /// </summary>
    public static bool LooksLikeReply(byte[] input)
        => TryLocate(input, null, out _, out _);

    /// <summary>
    /// 解析 <c>0x0900</c> 设备信息应答。字段表来自官方 <c>_K()</c>：
    /// <c>payload[10] = connectStatus、[11] = chargeStatus、[12] = batteryLevel、[13] = gameMode</c>。
    /// 应答长度不足 14（典型是设备在睡、只回 1 字节）或电量不可信时返回 false。
    /// </summary>
    public static bool TryParseDeviceInfo(byte[] frame, out int percent, out byte chargeStatus, out byte connectStatus)
    {
        percent = 0;
        chargeStatus = 0;
        connectStatus = 0;

        if (!TryLocate(frame, CmdDeviceInfo, out int p, out int len)) return false;
        if (len < DeviceInfoPayloadLength) return false;

        int value = frame[p + 12];
        if (!IsPlausiblePercent(value)) return false;

        connectStatus = frame[p + 10];
        chargeStatus = frame[p + 11];
        percent = value;
        return true;
    }

    /// <summary>
    /// 解析 <c>0x0600</c> 状态事件。<b><c>payload[0]</c> 与 <c>payload[1]</c> 是"本次带了哪些字段"的位图</b>
    /// （官方 <c>LA()</c> 把它们展开成 8 位二进制串再逐位判），对应关系：
    /// <list type="bullet">
    /// <item><c>payload[0]</c> bit1 → 电量，值在 <c>payload[3]</c></item>
    /// <item><c>payload[1]</c> bit1 → 充电状态，值在 <c>payload[12]</c></item>
    /// <item><c>payload[0]</c> bit0 → DPI 档位（<c>payload[2]</c>）、bit3 → 配置索引（<c>payload[6]</c>），本插件不用</item>
    /// </list>
    /// 两条都不带就返回 false（设备还发过别的 0x0600 子类型）。
    /// </summary>
    public static bool TryParseStatusEvent(byte[] frame, out K7StatusEvent evt)
    {
        evt = default;
        if (!TryLocate(frame, CmdStatusEvent, out int p, out int len)) return false;
        if (len < StatusEventPayloadLength) return false;

        byte flagsLo = frame[p];
        byte flagsHi = frame[p + 1];

        int? percent = null;
        if ((flagsLo & 0x02) != 0)
        {
            // 带了电量位但值不可信（0 = 设备还没算出来）就当没带，别把 0% 报出去
            int value = frame[p + 3];
            if (IsPlausiblePercent(value)) percent = value;
        }

        byte? chargeStatus = (flagsHi & 0x02) != 0 ? frame[p + 12] : null;

        if (percent == null && chargeStatus == null) return false;
        evt = new K7StatusEvent(percent, chargeStatus);
        return true;
    }

    /// <summary>
    /// 校验帧结构（report ID / version / 声明长度 / 命令字）并给出 payload 下标，失败返回 false。
    /// <paramref name="command"/> 为 null 时只校验帧头（"这是不是一条应答"）。
    ///
    /// **不校验校验字节**：设备应答用的不是请求那套 XOR（实测 0xFF 应答的校验位是 0），
    /// 官方 <c>_K</c> / <c>LA</c> 也从不校验。帧头 + 长度 + 命令字 + 值域几道关已经足够。
    /// </summary>
    private static bool TryLocate(byte[] frame, ushort? command, out int payloadOffset, out int payloadLength)
    {
        payloadOffset = 0;
        payloadLength = 0;

        foreach (int off in Offsets)
        {
            if (frame.Length < off + 8) continue;
            if (frame[off] != ReportId || frame[off + 1] != Version) continue;

            int declared = frame[off + 3];
            if (frame.Length < off + 8 + declared) continue;

            if (command.HasValue && (frame[off + 4] | (frame[off + 5] << 8)) != command.Value) continue;

            payloadOffset = off + 8;
            payloadLength = declared;
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
