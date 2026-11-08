using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Mchose;

/// <summary><c>0x0600</c> 状态事件的解析结果：设备一次只报一个字段，故两项各自可空。</summary>
public readonly record struct Mchose0x4DStatusEvent(int? Percent, byte? ChargeStatus);

/// <summary>
/// 功 能： 迈从 0x4D 协议的**唯一**编解码实现（帧构造 / 应答定位 / 公共字段解析），不碰句柄、不写日志。
/// 2026/11/08 20:30:00 xliu
/// 帧与字段取自官方 `main-CRYCuU75.js` 的 `getCommandBuffer` / `deviceInfoToObj` / `LA()`，见 mchose.md §3。
/// </summary>
public static class Mchose0x4DCodec
{
    /// <summary>运行时协议名。qhw 与 mouse-k7 合并后统一用它，不再区分两套实现。</summary>
    public const string ProtocolName = "mchose-0x4d";

    /// <summary>厂 ID（迈从新世代）。</summary>
    public const ushort VendorId = 0x3837;

    /// <summary>
    /// 8K 通用 dongle 的 PID（官方 productKey 尾部 <c>4120</c> = 0x1018），也是**唯一有真机指纹**的一颗。
    /// **不是型号标识**：K7 V2 全系、A7 V3 全系、K5 Pro/Ultra、R7 Ultra 共用它。
    /// </summary>
    public const ushort PidDongle8K = 0x1018;

    /// <summary>命令通道的 usagePage / usage（USB 路径里是 <c>mi_02&amp;col02</c>）。</summary>
    public const ushort UsagePage = 0xFF01;
    public const ushort Usage = 0x0001;

    /// <summary>report ID（也是帧首字节）。</summary>
    public const byte ReportId = 0x4D;

    /// <summary>帧的 version 字节，恒 <c>0x01</c>。</summary>
    public const byte Version = 0x01;

    /// <summary>请求帧 flags。**必须是 1**：官方只在 <c>flags === 1</c> 时才写校验字节。</summary>
    public const byte Flags = 0x01;

    /// <summary>帧总长（HID 报告含 report ID 首字节）。</summary>
    public const int FrameLength = 64;

    /// <summary>命令：读设备信息（应答里带 VID/PID、连接状态、充电状态、电量、游戏模式）。</summary>
    public const ushort CmdDeviceInfo = 0x0900;

    /// <summary>命令：电源/状态事件（设备**主动**上报，电量与充电状态都走这一条）。</summary>
    public const ushort CmdStatusEvent = 0x0600;

    /// <summary>设备信息应答的最小 payload 长度（要读到 <c>payload[13]</c>）。</summary>
    private const int DeviceInfoPayloadLength = 14;

    /// <summary>身份字段的最小 payload 长度：只要 <c>payload[0..3]</c> 的 VID/PID。</summary>
    private const int IdentityPayloadLength = 4;

    /// <summary>状态事件的最小 payload 长度（充电状态在 <c>payload[12]</c>）。</summary>
    private const int StatusEventPayloadLength = 13;

    /// <summary>业务码与序号：官方恒 0，不要自增（自增会让设备的应答匹配错位）。</summary>
    private const byte BizCode = 0x00;
    private const byte Sequence = 0x00;

    /// <summary>Windows 的 ReadInput 缓冲可能带/不带 report ID 前缀，两种偏移都试（与 A5/A7 解析器一致）。</summary>
    private static readonly int[] Offsets = { 0, 1 };

    /// <summary>
    /// 接口是不是**已验证**的 0x4D 命令通道（`0x1018` 的精确指纹：厂 ID + PID + 厂商用法 + 双向 64 字节）。
    /// 其余通用 dongle 没有真机指纹，走 <see cref="IsVendorTransportInterface"/> 的宽松判据。
    /// </summary>
    public static bool IsSupportedInterface(ushort vendorId, ushort productId,
        ushort usagePage, ushort usage, ushort inputLength, ushort outputLength)
        => vendorId == VendorId
           && productId == PidDongle8K
           && usagePage == UsagePage
           && usage == Usage
           && inputLength >= FrameLength
           && outputLength >= FrameLength;

    /// <summary>
    /// 未验证通用 dongle 的形态判据：厂商页 + 双向 64 字节。**刻意比 <see cref="IsSupportedInterface"/> 宽**——
    /// 那四颗 PID 的接口指纹没有真机证据，收紧会漏认、放宽到「同 VID 就试」会误认，这一档是折中。
    /// 只看 usagePage（usage 是集合号，同一颗 dongle 上 0x0001/0x0010/0x0074 都有）。
    /// </summary>
    public static bool IsVendorTransportInterface(ushort usagePage, ushort usage,
        ushort inputLength, ushort outputLength)
        => usagePage >= 0xFF00
           && inputLength >= FrameLength && outputLength >= FrameLength;

    /// <summary>读设备信息（电量 / 充电状态 / 连接状态 / 配对身份）的请求帧。</summary>
    public static byte[] BuildDeviceInfoQuery() => BuildFrame(CmdDeviceInfo);

    /// <summary>
    /// 构造 0x4D 帧：<c>4D 01 01 len cmdLo cmdHi 00 00 payload… chk</c>，补零到 64 字节。
    /// <b>chk = XOR(frame[2 .. 7+len])</b>，且**只有 <c>flags == 1</c> 才计算**——官方 <c>getCommandBuffer</c> 原样行为。
    /// </summary>
    public static byte[] BuildFrame(ushort command, params byte[] payload)
    {
        var frame = new byte[FrameLength];
        frame[0] = ReportId;
        frame[1] = Version;
        frame[2] = Flags;
        frame[3] = (byte)payload.Length;
        frame[4] = (byte)(command & 0xFF);
        frame[5] = (byte)(command >> 8);
        frame[6] = BizCode;
        frame[7] = Sequence;
        Array.Copy(payload, 0, frame, 8, payload.Length);

        byte xor = 0;
        for (int i = 2; i < 8 + payload.Length; i++) xor ^= frame[i];
        frame[8 + payload.Length] = xor;
        return frame;
    }

    /// <summary>
    /// 这一帧是不是"设备说话了"（帧头 <c>4D 01</c> 且声明长度装得下就算）。
    /// 判"有没有应答"必须用它：鼠标在睡时 dongle 只回一条 payload 1 字节的 <c>FF</c>，
    /// 把它当"没应答"记账会让睡着的设备三轮后被判离线并熔断停写。
    /// </summary>
    public static bool LooksLikeReply(byte[] input)
        => TryLocate(input, null, out _, out _);

    /// <summary>
    /// 解析 <c>0x0900</c> 设备信息应答。字段表来自官方 <c>deviceInfoToObj</c>：
    /// <c>payload[10]=connectStatus、[11]=chargeStatus、[12]=batteryLevel、[13]=gameMode</c>。
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
    /// 解析 <c>0x0900</c> 里的配对身份：<c>payload[0..1] = VID（LE）</c>、<c>[2..3] = PID（LE）</c>。
    /// 这是**配对本体**的 ID，不是 dongle 的；长度不足 4（设备在睡只回 1 字节 0xFF）一律不采信。
    /// </summary>
    public static bool TryParseDeviceIdentity(byte[] frame, out ushort vendorId, out ushort productId)
    {
        vendorId = 0;
        productId = 0;

        if (!TryLocate(frame, CmdDeviceInfo, out int p, out int len)) return false;
        if (len < IdentityPayloadLength) return false;

        vendorId = (ushort)(frame[p] | frame[p + 1] << 8);
        productId = (ushort)(frame[p + 2] | frame[p + 3] << 8);
        return true;
    }

    /// <summary>
    /// 解析 <c>0x0600</c> 状态事件。<b><c>payload[0]</c> 与 <c>payload[1]</c> 是"本次带了哪些字段"的位图</b>：
    /// <c>[0]</c> bit1 → 电量（值在 <c>[3]</c>）、<c>[1]</c> bit1 → 充电状态（值在 <c>[12]</c>）。
    /// 两条都不带就返回 false（设备还发过别的 0x0600 子类型）。
    /// </summary>
    public static bool TryParseStatusEvent(byte[] frame, out Mchose0x4DStatusEvent evt)
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
        evt = new Mchose0x4DStatusEvent(percent, chargeStatus);
        return true;
    }

    /// <summary>
    /// <c>connectStatus</c> → 统一连接方式。官方只给了 `1=有线`、`2=无线`（mchose.md §3.3）；
    /// 0 与其它值官方没语义，一律 Unknown——不猜"未连接"，也不当在线判据。
    /// </summary>
    public static ConnectionKind ToConnectionKind(byte connectStatus) => connectStatus switch
    {
        1 => ConnectionKind.Wired,
        2 => ConnectionKind.Wireless24G,
        _ => ConnectionKind.Unknown,
    };

    /// <summary>
    /// <c>chargeStatus</c> → 统一充电状态：`0=未充 / 1=充电中 / 2=充满`（官方枚举，mchose.md §3.3），
    /// 其余值 Unknown——不默认成"未充电"；未充电且 100% 的收尾走 <see cref="ChargingStateMap"/>。
    /// </summary>
    public static ChargingState ToChargingState(byte chargeStatus, int percent) => chargeStatus switch
    {
        0 => ChargingStateMap.FromChargingFlag(charging: false, percent),
        1 => ChargingStateMap.FromChargingFlag(charging: true, percent),
        2 => ChargingState.Full,
        _ => ChargingState.Unknown,
    };

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
