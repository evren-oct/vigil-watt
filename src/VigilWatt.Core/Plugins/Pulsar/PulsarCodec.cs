namespace VigilWatt.Core.Plugins.Pulsar;

/// <summary>
/// 功 能： 派世 (Pulsar) 17 字节厂商帧（report 0x08）的纯编解码，不碰句柄、不写日志，便于单测。
/// 2026/10/18 17:24:10 sheng
/// 校验与命令表来自 Linux `hid-pulsar` 驱动，见 docs/hardware/brands/pulsar.md §1。
/// </summary>
internal static class PulsarCodec
{
    /// <summary>厂商集合 mi_01&amp;col05（usage 0xFF02，17 字节双向）的 report ID。</summary>
    public const byte ReportId = 0x08;

    public const int FrameLength = 17;

    public const byte CmdStatus = 0x03;
    public const byte CmdPower = 0x04;

    /// <summary>校验种子：应答末字节 = 0x55 减去前 16 字节之和（mod 256）。</summary>
    public const byte ChecksumSeed = 0x55;

    /// <summary>构造命令帧：载荷全 0，末字节放校验。</summary>
    public static byte[] BuildFrame(byte command)
    {
        var frame = new byte[FrameLength];
        frame[0] = ReportId;
        frame[1] = command;
        frame[^1] = Checksum(frame);
        return frame;
    }

    /// <summary>按驱动同款公式算校验（末字节自身不参与）。</summary>
    public static byte Checksum(ReadOnlySpan<byte> frame)
    {
        int sum = 0;
        for (int i = 0; i < frame.Length - 1; i++) sum += frame[i];
        return (byte)((ChecksumSeed - sum) & 0xFF);
    }

    /// <summary>应答必须是同 ID、同命令、校验正确——鼠标休眠时接口可能吐半截帧。</summary>
    public static bool TryParse(byte[] frame, byte expectedCommand, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        if (frame.Length != FrameLength) return false;
        if (frame[0] != ReportId || frame[1] != expectedCommand) return false;
        if (frame[^1] != Checksum(frame)) return false;
        payload = frame;
        return true;
    }

    /// <summary>STATUS 应答的 [6]：1 = 设备在线，0 = dongle 在位但设备没连上。</summary>
    public static bool TryParseOnline(byte[] frame, out bool online)
    {
        online = false;
        if (frame.Length != FrameLength) return false;
        online = frame[6] == 1;
        return true;
    }

    /// <summary>POWER 应答：[6] 电量百分比、[7] 充电中、[8..9] 电压 mV 大端。</summary>
    public static bool TryParsePower(byte[] frame, out int percent, out bool charging, out int millivolts)
    {
        percent = 0;
        charging = false;
        millivolts = 0;
        if (frame.Length != FrameLength) return false;
        if (frame[6] > 100) return false;
        percent = frame[6];
        charging = frame[7] == 1;
        millivolts = (frame[8] << 8) | frame[9];
        return true;
    }
}
