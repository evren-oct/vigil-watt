namespace VigilWatt.Core.Plugins.Mchose;

/// <summary>
/// 功 能： 迈从 0x4D 帧（G98 V3 / K87 V3 这一代键盘与通用 dongle）的纯编解码，不碰句柄、不写日志。
/// 2026/11/16 00:55:00 Administrator
/// 帧与字段取自官方 M HUB WEB `main-MopJAj_H.js` 的 `getCommandBuffer` / `deviceInfoToObj`，见 docs/hardware/mchose.md §2.4。
/// </summary>
public static class Mchose0x4DCodec
{
    /// <summary>命令通道 report ID，也是帧首字节。</summary>
    public const byte ReportId = 0x4D;

    /// <summary>帧总长（HID 报告含 report ID 首字节）。</summary>
    public const int FrameLength = 64;

    /// <summary>读设备信息（应答带连接、充电、电量）。</summary>
    public const ushort CmdDeviceInfo = 0x0900;

    /// <summary>
    /// 这一代键盘的厂商页。官方 `getProtocol` 按「PID + 产品名」把 4117（0x1015）判成 qhw；
    /// 本体与 2.4G dongle 的 PID 分别是 0x2025/0x2026 与 0x1015/0x1017，见 docs/hardware/mchose.md §2.4。
    /// </summary>
    public const ushort UsagePage = 0xFF80;

    /// <summary>厂商页里的集合号，四颗 PID 都是 1。</summary>
    public const ushort Usage = 0x0001;

    /// <summary>迈从新世代厂 ID。写死在这里而不是引用分类器：本类是纯编解码，要能脱离分类器单独进单测。</summary>
    private const ushort MchoseVid = 0x3837;

    /// <summary>注册在这条通道上的 PID：2.4G dongle（1015/1017）与键盘本体（2025/2026）。</summary>
    private static readonly ushort[] KeyboardPids = { 0x1015, 0x1017, 0x2025, 0x2026 };

    /// <summary>应答里 payload 起点的下标（前 8 字节是帧头）。</summary>
    private const int PayloadOffset = 8;

    /// <summary>设备信息应答的最小 payload 长度：要读到 payload[12]。</summary>
    private const int DeviceInfoPayloadLength = 14;

    /// <summary>Windows 的 ReadInput 缓冲可能带/不带 report ID 前缀，两种偏移都试。</summary>
    private static readonly int[] Offsets = { 0, 1 };

    /// <summary>
    /// 接口是不是这一代键盘的 0x4D 通道。
    /// **必须排在「≥60 字节只走鼠标」之前**：它与鼠标 V2 同为 64/64，落到那条分支只会白等 E2 帧。
    /// </summary>
    public static bool IsKeyboardInterface(ushort vendorId, ushort productId,
        ushort usagePage, ushort usage, ushort inputLength, ushort outputLength)
        => vendorId == MchoseVid
           && System.Array.IndexOf(KeyboardPids, productId) >= 0
           && usagePage == UsagePage && usage == Usage
           && inputLength >= FrameLength && outputLength >= FrameLength;

    /// <summary>
    /// 读设备信息的请求帧：`4D 01 01 00 00 09 00 00 08 00…`。
    /// **没有任何握手帧**——同理，也绝不能先发 0x4D AA55…（实测会把这类 dongle 切到另一个 PID）。
    /// </summary>
    public static byte[] BuildDeviceInfoQuery()
    {
        var frame = new byte[FrameLength];
        frame[0] = ReportId;
        frame[1] = 0x01;   // version
        frame[2] = 0x01;   // flags：官方只在 flags==1 时写校验字节
        frame[3] = 0x00;   // payload 长度
        frame[4] = (byte)(CmdDeviceInfo & 0xFF);
        frame[5] = (byte)(CmdDeviceInfo >> 8);
        frame[6] = 0x00;   // bizCode
        frame[7] = 0x00;   // sequence：官方恒 0，自增会让应答匹配错位

        byte xor = 0;
        for (int i = 2; i < PayloadOffset; i++) xor ^= frame[i];
        frame[PayloadOffset] = xor;
        return frame;
    }

    /// <summary>
    /// 解析 0x0900 应答：payload[10]=连接（1 有线 / 2 无线）、[11]=充电（0/1/2）、[12]=电量%。
    /// 电量不在 1..100 一律拒绝——宁可没有读数，也不报一个错的值。
    /// </summary>
    public static bool TryParseDeviceInfo(byte[] frame, out int percent, out byte chargeStatus, out byte connectStatus)
    {
        percent = 0;
        chargeStatus = 0;
        connectStatus = 0;

        foreach (int off in Offsets)
        {
            if (frame.Length < off + PayloadOffset + DeviceInfoPayloadLength) continue;
            if (frame[off] != ReportId || frame[off + 1] != 0x01) continue;
            if (frame[off + 4] != (CmdDeviceInfo & 0xFF) || frame[off + 5] != (CmdDeviceInfo >> 8)) continue;
            if (frame[off + 3] < DeviceInfoPayloadLength) continue;

            byte value = frame[off + PayloadOffset + 12];
            if (value == 0 || value > 100) continue;

            connectStatus = frame[off + PayloadOffset + 10];
            chargeStatus = frame[off + PayloadOffset + 11];
            percent = value;
            return true;
        }
        return false;
    }
}
