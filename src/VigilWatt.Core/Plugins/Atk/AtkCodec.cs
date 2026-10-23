namespace VigilWatt.Core.Plugins.Atk;

using VigilWatt.Core.Models;

/// <summary>
/// 功 能： ATK/VXE 鼠标电量查询帧的纯编解码——不碰句柄、不写日志，便于单测。
/// 2026/10/20 22:06:00 Yang
/// 帧结构、校验算法、休眠占位帧实测依据见 docs/hardware/protocols.md §5。
/// </summary>
internal static class AtkCodec
{
    /// <summary>电量查询与响应的 report ID。</summary>
    public const byte ReportId = 0x08;

    /// <summary>电量命令字（请求与响应都以此回显）。</summary>
    public const byte BatteryCmd = 0x04;

    /// <summary>请求 payload 长度（不含 report ID）。</summary>
    public const int PayloadLength = 16;

    /// <summary>校验种子：尾字节 = (0x55 − 前 15 字节之和) & 0xFF。</summary>
    public const byte ChecksumSeed = 0x55;

    /// <summary>电量百分比所在字节下标（相对 report ID）。</summary>
    public const int LevelOffset = 6;

    /// <summary>充电标志所在字节下标（0 = 未充电，非 0 = 有线/充电中）。</summary>
    public const int ChargeOffset = 7;

    /// <summary>电池电压起始字节下标（大端 16 位，单位 mV）。</summary>
    public const int MillivoltOffset = 8;

    /// <summary>无效电量下界：≤1 是休眠/关机占位值，不是真实电量。</summary>
    public const int InvalidFloor = 1;

    /// <summary>构造 17 字节电量查询帧。</summary>
    public static byte[] BuildQuery()
    {
        var frame = new byte[1 + PayloadLength];
        frame[0] = ReportId;
        frame[1] = BatteryCmd;
        int sum = ReportId;
        for (int i = 0; i < PayloadLength - 1; i++) sum += frame[1 + i];
        frame[PayloadLength] = (byte)((ChecksumSeed - (sum & 0xFF)) & 0xFF);
        return frame;
    }

    /// <summary>解析电量响应；null = 不是本协议报文，或休眠占位帧（≤1）。</summary>
    public static (int Percent, ChargingState Charging)? TryParseReply(byte[]? input)
    {
        int off = OffsetOf(input);
        if (off < 0 || off + ChargeOffset >= input!.Length) return null;

        int pct = input[off + LevelOffset];
        if (pct is < 0 or > 100 || pct <= InvalidFloor) return null;

        ChargingState charging = input[off + ChargeOffset] == 0
            ? (pct >= 100 ? ChargingState.Full : ChargingState.Discharging)
            : ChargingState.Charging;
        return (pct, charging);
    }

    /// <summary>电池电压 mV（响应 [8..9] 大端）；非本协议帧返回 null。</summary>
    public static int? ReadMillivolts(byte[]? input)
    {
        int off = OffsetOf(input);
        if (off < 0 || off + MillivoltOffset + 1 >= input!.Length) return null;
        return (input[off + MillivoltOffset] << 8) | input[off + MillivoltOffset + 1];
    }

    /// <summary>定位 report ID：Windows 缓冲首字节即 ID，但容忍被剥掉 1 字节的读路径；不匹配返回 -1。</summary>
    private static int OffsetOf(byte[]? input)
    {
        if (input is not { Length: >= 4 }) return -1;
        foreach (int off in new[] { 0, 1 })
        {
            if (input[off] == ReportId && input[off + 1] == BatteryCmd && input.Length > off + 1)
                return off;
        }
        return -1;
    }
}