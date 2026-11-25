using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Vgn;

/// <summary>
/// 功 能： VGN 蜻蜓 F2 大师+（y2）私有协议的纯帧编解码：电量查询与应答解析，零 I/O。
/// 2026/10/21 00:00:00
/// 帧结构、来源与真机样本见 docs/hardware/brands/vgn.md。
/// </summary>
internal static class VgnY2Codec
{
    /// <summary>0x3554 = Compx 共用号段（狼蛛 F99 Pro 与 VGN 蜻蜓 F2 都在用）。</summary>
    public const ushort Vid = 0x3554;

    /// <summary>蜻蜓 F2 大师+ 的 2.4G 接收器（本机实测）。</summary>
    public const ushort PidReceiver = 0xFB56;

    /// <summary>蜻蜓 F2 大师 的有线 PID（VHUB 设备表 64343）；同族同协议，**本机未复测**。</summary>
    public const ushort PidWired = 0xFB57;

    /// <summary>厂商通道的 usagePage（65346）。</summary>
    public const ushort VendorUsagePage = 0xFF02;

    /// <summary>厂商通道的 usage。</summary>
    public const ushort VendorUsage = 0x0002;

    /// <summary>官方驱动的 report ID。</summary>
    public const byte ReportId = 0x08;

    /// <summary>payload 长度（不含 report ID）。</summary>
    public const int PayloadLength = 16;

    /// <summary>Windows 侧收发缓冲长度（该接口 in=17 / out=17）。</summary>
    public const int FrameLength = PayloadLength + 1;

    /// <summary>命令：设备是否在线（应答 [5]=1 在线）。</summary>
    public const byte CmdDeviceOnline = 0x03;

    /// <summary>命令：读电量（应答 [5]=电量、[6]=充电、[7..8]=电压）。</summary>
    public const byte CmdBatteryLevel = 0x04;

    /// <summary>校验基数：官方实现里 crc = 85 - sum，再减 report ID 落到报文末字节。</summary>
    private const int ChecksumBase = 85;

    /// <summary>
    /// 设备匹配（纯数据，方便单测）：VID + 两个 PID + 厂商 usage + **恰好** 17/17。
    /// 长度必须精确相等而不是 "≥"：同 VID 下 F99 Pro 是 20/20，长度放宽就会把 17 字节帧
    /// 写进一条 20 字节的通道里；宁可认领失败（设备只是不显示），也不要往别的协议里灌字节。
    /// </summary>
    public static bool IsMatch(ushort vid, ushort pid, ushort usagePage, ushort usage,
        int inputLength, int outputLength)
    {
        if (vid != Vid) return false;
        if (pid != PidReceiver && pid != PidWired) return false;
        if (usagePage != VendorUsagePage || usage != VendorUsage) return false;
        return inputLength == FrameLength && outputLength == FrameLength;
    }

    /// <summary>构造一条 17 字节命令帧（带 report ID 前缀，可直接 WriteOutput）。</summary>
    public static byte[] BuildCommand(byte command, byte reportId = ReportId)
    {
        var frame = new byte[FrameLength];
        frame[0] = reportId;
        frame[1] = command;
        frame[^1] = Checksum(frame.AsSpan(1), reportId);
        return frame;
    }

    /// <summary>校验和：先 (85 - 前 15 字节之和) &amp; 0xFF，再减 report ID &amp; 0xFF。</summary>
    public static byte Checksum(ReadOnlySpan<byte> payload, byte reportId = ReportId)
    {
        int sum = 0;
        for (int i = 0; i < PayloadLength - 1 && i < payload.Length; i++) sum += payload[i];
        return (byte)((ChecksumBase - (sum & 0xFF) - reportId) & 0xFF);
    }

    /// <summary>
    /// 解析电量应答。允许"带 report ID"（17 字节）与"纯 payload"（16 字节）两种形态，
    /// 命令回显、成功标志、校验和三者全部对上才采信。
    /// </summary>
    public static bool TryParseBattery(ReadOnlySpan<byte> frame, out int percent,
        out ChargingState charging, out int voltageMv)
    {
        percent = 0;
        charging = ChargingState.Unknown;
        voltageMv = 0;

        foreach (int start in StartOffsets(frame))
        {
            if (frame.Length < start + PayloadLength) continue;
            var payload = frame.Slice(start, PayloadLength);

            if (payload[0] != CmdBatteryLevel || payload[1] != 0) continue;
            if (payload[^1] != Checksum(payload)) continue;

            int pct = payload[5];
            // 0 在协议层就是"没读到"（设备休眠/未连），>100 只可能是错位，两者都不采信。
            if (pct is 0 or > 100) continue;

            percent = pct;
            bool isCharging = payload[6] == 1;
            charging = isCharging
                ? (pct >= 100 ? ChargingState.Full : ChargingState.Charging)
                : ChargingState.Discharging;
            voltageMv = (payload[7] << 8) | payload[8];
            return true;
        }
        return false;
    }

    /// <summary>解析在线应答（<c>DeviceOnLine</c>）：payload[5] == 1 视为在线。</summary>
    public static bool TryParseOnline(ReadOnlySpan<byte> frame, out bool online)
    {
        online = false;
        foreach (int start in StartOffsets(frame))
        {
            if (frame.Length < start + PayloadLength) continue;
            var payload = frame.Slice(start, PayloadLength);
            if (payload[0] != CmdDeviceOnline || payload[1] != 0) continue;
            if (payload[^1] != Checksum(payload)) continue;
            online = payload[5] == 1;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 先试"带 report ID"（首字节等于 0x08），再试"纯 payload"。
    /// 两种形态都要覆盖：WebHID 给的是后者，Windows ReadInput 给的是前者。
    /// </summary>
    private static int[] StartOffsets(ReadOnlySpan<byte> frame)
        => frame.Length >= FrameLength && frame[0] == ReportId ? new[] { 1, 0 } : new[] { 0 };
}
