namespace VigilWatt.Core.Plugins.Eweadn;

/// <summary>
/// 功 能： 前行者 X87 键盘 2.4G 接收器的电量查询帧编解码——不碰句柄、不写日志，便于单测。
/// 2026/10/18 13:40:00 Admin
/// 帧结构、校验和位置与实测依据见 docs/hardware/brands/eweadn.md §1.1。
/// </summary>
internal static class EweadnKeyboardCodec
{
    /// <summary>正文首字节标记（官方 DeviceDriver.exe 0x4364e0 把 buf[1] 置 0x20）。</summary>
    public const byte QueryMarker0 = 0x20;

    /// <summary>正文次字节标记（官方 buf[2] = 0x01）。</summary>
    public const byte QueryMarker1 = 0x01;

    /// <summary>整帧长度（含 report ID）：官方 memset 65 字节但只发前 33 字节。</summary>
    public const int FrameLength = 33;

    /// <summary>校验和偏移：官方对 65 字节求和后写入 int 下标 8（即字节 32）。</summary>
    public const int ChecksumOffset = 32;

    /// <summary>正文里电量所在偏移（官方判据 local_4c[3]）。</summary>
    public const int BatteryOffset = 3;

    /// <summary>有效电量上界：官方 >100 一律截为 100。</summary>
    public const int MaxPercent = 100;

    /// <summary>构造 33 字节查询帧：report ID 0x00 + 标记 20 01 + 尾部校验和。</summary>
    public static byte[] BuildQuery()
    {
        var frame = new byte[FrameLength];
        frame[1] = QueryMarker0;
        frame[2] = QueryMarker1;
        frame[ChecksumOffset] = Checksum(frame);
        return frame;
    }

    /// <summary>校验和 = 全部字节求和取低 8 位；调用时校验和位置必须还是 0，否则会自我累加。</summary>
    public static byte Checksum(byte[] frame)
    {
        int sum = 0;
        foreach (byte b in frame) sum += b;
        return (byte)(sum & 0xFF);
    }

    /// <summary>
    /// 解析电量响应：返回百分比，非本协议或电量无效返回 null。
    /// 兼容带 report ID 前缀（下标 0）与不带（下标 1）两种布局。
    /// </summary>
    public static int? ParseBattery(byte[] input)
    {
        foreach (int off in new[] { 0, 1 })
        {
            if (input.Length < off + BatteryOffset + 1) continue;
            if (input[off] != QueryMarker0) continue;
            if (input[off + 1] != QueryMarker1) continue;

            byte raw = input[off + BatteryOffset];
            if (raw == 0) return null;   // 0 = 设备未就绪，不是电量
            return raw > MaxPercent ? MaxPercent : raw;
        }
        return null;
    }
}
