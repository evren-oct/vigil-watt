namespace VigilWatt.Core.Plugins.Atk;

using VigilWatt.Core.Models;

/// <summary>
/// 功 能： ATK 新固件 FF05 通道的 64 字节寄存器读（373B:1216），与 FF02 的 17 字节 op 帧无共用字段。
/// 2026/11/01 17:05 xliu
/// 帧样本来自社区实测（2026-10-31），本机无该设备、未复测；帧格式与判据见 docs/hardware/atk.md 的 FF05 一节。
/// </summary>
internal static class AtkFf05Codec
{
    /// <summary>电量口的 usage page；老型号是 0xFF02，新固件换成这个。</summary>
    public const ushort UsagePage = 0xFF05;

    /// <summary>唯一登记过的 FF05 设备。必须精确匹配：别对其他 FF05 口发帧（safety.md §3 规则 1）。</summary>
    public const ushort ProductId = 0x1216;

    /// <summary>该接口唯一的 output report ID。</summary>
    public const byte ReportId = 0x08;

    /// <summary>线上帧长度（含 report ID）；in / out 都是 64。</summary>
    public const int ReportLength = 64;

    /// <summary>响应固定标记；普通鼠标报文凑不出后面的寄存器回显。</summary>
    private const byte ReplyMark = 0x72;

    /// <summary>电量寄存器 0x0107（小端），请求与响应都以它定位。</summary>
    private const byte RegisterLow = 0x07;
    private const byte RegisterHigh = 0x01;

    /// <summary>相对 0x72 标记的偏移：电量 1 字节、电压 2 字节。</summary>
    private const int PercentOffset = 6;
    private const int MillivoltOffset = 7;

    /// <summary>构造 64 字节电量查询帧；前 8 字节有意义，其余补 0。</summary>
    public static byte[] BuildQuery()
    {
        var frame = new byte[ReportLength];
        frame[0] = ReportId;
        frame[1] = 0x7D;
        frame[2] = ReplyMark;
        frame[3] = 0x02;
        frame[4] = 0x00;
        frame[5] = 0x01;
        frame[6] = RegisterLow;
        frame[7] = RegisterHigh;
        return frame;
    }

    /// <summary>
    /// 四样同时精确匹配才认领：usagePage + 登记过的 PID + 64 进 64 出。
    /// 不做"FF05 就发帧"的宽判，避免对同厂商其他 FF05 集合写数据。
    /// </summary>
    public static bool IsSupportedInterface(ushort usagePage, ushort productId,
        ushort inputLength, ushort outputLength)
        => usagePage == UsagePage
           && productId == ProductId
           && inputLength == ReportLength
           && outputLength == ReportLength;

    /// <summary>
    /// 解析电量响应；null = 不是本协议报文，或电量是休眠/关机占位。
    /// 判定靠"标记 + 寄存器回显"两项对上，比单看字节范围可靠。
    /// </summary>
    public static (int Percent, int Millivolts, ChargingState Charging)? TryParse(byte[]? input)
    {
        if (!TryLocate(input, out var frame)) return null;

        int percent = frame[PercentOffset];
        if (percent is < 2 or > 100) return null;      // 0 / 1 是休眠、关机占位

        // ⚠ 小端，与 AtkCodec 的大端相反：C1 0E = 3777 mV。别顺手把两边统一。
        int millivolts = frame[MillivoltOffset] | (frame[MillivoltOffset + 1] << 8);

        // 本帧不带充电位：读它要再发一条 0x010F 查询，超出"每周期 1 帧"（safety.md §3 规则 4）。
        // 故只按满电推断，充电中会显示为放电——待补第二条查询时再修。
        ChargingState charging = percent >= 100 ? ChargingState.Full : ChargingState.Discharging;
        return (percent, millivolts, charging);
    }

    /// <summary>
    /// 定位响应头。report ID 在不在缓冲里都可能（读路径不同会差一个字节），
    /// 故按"标记 + 寄存器回显"搜索而不是写死下标。
    /// </summary>
    private static bool TryLocate(byte[]? input, out ReadOnlySpan<byte> frame)
    {
        frame = default;
        if (input is null) return false;

        for (int i = 0; i + MillivoltOffset + 1 < input.Length; i++)
        {
            if (input[i] != ReplyMark || input[i + 1] != 0x00) continue;
            if (input[i + 4] != RegisterLow || input[i + 5] != RegisterHigh) continue;
            frame = input.AsSpan(i);
            return true;
        }
        return false;
    }
}
