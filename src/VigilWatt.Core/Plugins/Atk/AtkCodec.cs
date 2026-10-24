namespace VigilWatt.Core.Plugins.Atk;

using VigilWatt.Core.Models;

/// <summary>
/// 功 能： ATK/VXE 鼠标电量查询帧的纯编解码——不碰句柄、不写日志，便于单测。
/// 2026/10/20 22:06:00 Yang
/// 帧结构、校验算法、休眠占位帧实测依据见 docs/hardware/atk.md。
/// </summary>
internal static class AtkCodec
{
    /// <summary>电量查询与响应的 report ID。</summary>
    public const byte ReportId = 0x08;

    /// <summary>电量命令字（请求与响应都以此回显）。</summary>
    public const byte BatteryCmd = 0x04;

    /// <summary>op 帧长度（不含 report ID）：cmd + 14 数据 + 1 校验。</summary>
    public const int PayloadLength = 16;

    /// <summary>线上帧长度（含 report ID）。</summary>
    public const int ReportLength = 17;

    /// <summary>校验基数：chk = (0x4D − sum(op[0..14])) & 0xFF。</summary>
    public const byte ChecksumSeed = 0x4D;

    /// <summary>电量百分比所在字节下标（相对 op 帧起点）。</summary>
    public const int LevelOffset = 5;

    /// <summary>充电标志所在字节下标（0 = 未充电，非 0 = 有线/充电中）。</summary>
    public const int ChargeOffset = 6;

    /// <summary>电池电压起始字节下标（大端 16 位，单位 mV）。</summary>
    public const int MillivoltOffset = 7;

    /// <summary>无效电量下界：≤1 是休眠/关机占位值，不是真实电量。</summary>
    public const int InvalidFloor = 1;

    /// <summary>构造 17 字节电量查询帧（report ID + op 帧）。</summary>
    public static byte[] BuildQuery()
    {
        var frame = new byte[ReportLength];
        frame[0] = ReportId;
        frame[1] = BatteryCmd;
        int sum = 0;
        for (int i = 0; i < PayloadLength - 1; i++) sum += frame[1 + i];
        frame[PayloadLength] = (byte)((ChecksumSeed - (sum & 0xFF)) & 0xFF);
        return frame;
    }

    /// <summary>
    /// 运行时校验和。**必须校验通过才采信**——固件会在同一设备上混发普通鼠标报文与
    /// 厂商私有集合帧，只看 cmd + 电量范围会把非电量帧读成假读数。
    /// </summary>
    public static bool ChecksumValid(ReadOnlySpan<byte> op)
    {
        if (op.Length < PayloadLength) return false;
        int sum = 0;
        for (int i = 0; i < PayloadLength - 1; i++) sum += op[i];
        return op[PayloadLength - 1] == (byte)((ChecksumSeed - (sum & 0xFF)) & 0xFF);
    }

    /// <summary>解析电量响应；null = 不是本协议报文，或校验不过，或休眠占位帧（≤1）。</summary>
    public static (int Percent, ChargingState Charging)? TryParseReply(byte[]? input)
    {
        if (!TryGetOpFrame(input, out var op)) return null;

        int pct = op[LevelOffset];
        if (pct is < 0 or > 100 || pct <= InvalidFloor) return null;

        ChargingState charging = op[ChargeOffset] == 0
            ? (pct >= 100 ? ChargingState.Full : ChargingState.Discharging)
            : ChargingState.Charging;
        return (pct, charging);
    }

    /// <summary>电池电压 mV（op 帧 [7..8] 大端）；非本协议帧返回 null。</summary>
    public static int? ReadMillivolts(byte[]? input)
    {
        if (!TryGetOpFrame(input, out var op)) return null;
        return (op[MillivoltOffset] << 8) | op[MillivoltOffset + 1];
    }

    /// <summary>
    /// 严格接口判据：17 字节 in / 17 字节 out 才可能承载电量帧。
    /// ⚠ 只对**接收器**用严格判据——本体的接口长度未实测（插充电线时才枚举出来）。
    /// </summary>
    public static bool IsSupportedInterface(ushort inputLength, ushort outputLength)
        => inputLength == ReportLength && outputLength == ReportLength;

    /// <summary>
    /// 定位 op 帧：候选偏移逐个试，**以校验和通过为准**（自证），不靠固定位置猜。
    /// report ID 在不在缓冲里都可能，解析时两种偏移都试。
    /// </summary>
    private static bool TryGetOpFrame(byte[]? input, out ReadOnlySpan<byte> op)
    {
        op = default;
        if (input is null) return false;

        // 候选偏移：1 = 含 report ID；0 = report ID 被剥掉；2 = 前置占位字节
        foreach (int off in (int[])[1, 0, 2])
        {
            if (input.Length < off + PayloadLength) continue;
            if (input[off] != BatteryCmd) continue;
            if (!ChecksumValid(input.AsSpan(off, PayloadLength))) continue;

            op = input.AsSpan(off, PayloadLength);
            return true;
        }
        return false;
    }
}
