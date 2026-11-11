namespace VigilWatt.Core.Plugins.Ilovbee;

/// <summary>
/// 功 能： 解析 ilovbee b98 的 20 字节被动状态帧（`13 0A 01 00 04 &lt;事件&gt; &lt;电量&gt; &lt;标志&gt; … &lt;和校验&gt;`）。
/// 2026/11/01 17:32:05 20211
/// 帧布局、事件字与校验范围见 docs/hardware/devices/ilovbee.md §1；样本出自 issue #68。
/// </summary>
internal static class IlovbeeCodec
{
    public const ushort VendorId = 0x258A;
    public const ushort ReceiverPid = 0x01BB;

    /// <summary>状态帧所在的厂商集合；同一接收器的其它集合不推电量帧（ilovbee.md §1.1）。</summary>
    public const ushort VendorUsagePage = 0xFF02;
    public const ushort VendorUsage = 0x0002;

    /// <summary>线上整帧长度（含 report ID 0x13）。</summary>
    public const int FrameLength = 20;

    private const byte ReportId = 0x13;
    private const int EventOffset = 5;
    private const int PercentOffset = 6;
    private const int FlagsOffset = 7;
    private const int ChecksumOffset = 19;

    private const byte EventBattery = 0x05;
    private const byte EventPower = 0x02;

    /// <summary>帧的用途；<see cref="Battery"/> 之外的帧都不携带可信电量。</summary>
    public enum FrameKind
    {
        Battery,

        /// <summary>关机帧：`[6]` 恒为 0，不是 0% 电量（ilovbee.md §1.2）。</summary>
        PowerOff,

        /// <summary>开机帧：`[6]` 恒为 1，不是 1% 电量。</summary>
        PowerOn,
    }

    /// <summary>
    /// 解析一帧；false = 不是本协议帧、校验和不对或读数越界。
    /// <paramref name="chargingEdge"/> 只在**刚插上充电器那一帧**为 true：`[7]` bit0 是事件位，
    /// 后续帧立刻回 0，且该帧的 `[6]` 是占位值（实测 100，实际 75），调用方不得采信其电量。
    /// </summary>
    public static bool TryParse(byte[] input, out FrameKind kind, out int percent, out bool chargingEdge)
    {
        kind = FrameKind.Battery;
        percent = 0;
        chargingEdge = false;

        if (input.Length < FrameLength) return false;
        if (input[0] != ReportId) return false;
        if (!ChecksumMatches(input)) return false;

        byte raw = input[PercentOffset];
        switch (input[EventOffset])
        {
            case EventPower when raw <= 1:
                kind = raw == 0 ? FrameKind.PowerOff : FrameKind.PowerOn;
                return true;
            case EventBattery when raw <= 100:
                kind = FrameKind.Battery;
                percent = raw;
                chargingEdge = (input[FlagsOffset] & 0x01) != 0;
                return true;
            default:
                return false;
        }
    }

    /// <summary>校验和 = 前 19 字节按 8 位求和（report ID 0x13 也计入），见 ilovbee.md §1.3。</summary>
    private static bool ChecksumMatches(byte[] input)
    {
        int sum = 0;
        for (int i = 0; i < ChecksumOffset; i++) sum += input[i];
        return (byte)sum == input[ChecksumOffset];
    }
}
