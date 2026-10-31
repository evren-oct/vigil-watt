namespace VigilWatt.Core.Plugins.Langtu;

/// <summary>
/// 功 能： 解析狼途 T98 的 65 字节被动状态帧（`00 AA 55 CC 33 0E &lt;pct&gt; 01`）。
/// 2026/10/31 20:32:00 20211
/// 帧布局、0xFF 哨兵与休眠静音实测见 docs/hardware/langtu.md §1。
/// </summary>
internal static class LangtuT98Codec
{
    public const ushort VendorId = 0x1A2C;
    public const ushort ReceiverPid = 0x8FFF;

    /// <summary>状态帧所在集合；同一接收器的 MI_03 通知集合全程静音，不要认（langtu.md §1.1）。</summary>
    public const ushort VendorUsagePage = 0xFF02;
    public const ushort VendorUsage = 0x0002;

    /// <summary>线上整帧长度（含 report ID 0）。</summary>
    public const int FrameLength = 65;

    private const byte Sync0 = 0xAA;
    private const byte Sync1 = 0x55;
    private const byte Cmd = 0x0E;

    /// <summary>命令字相对同步头的偏移；电量紧跟其后，再后一字节恒为 0x01（不是充电位）。</summary>
    private const int CmdOffset = 4;
    private const int PercentOffset = 5;

    /// <summary>插拔充电线瞬间固件发 0xFF；不丢会显示 255%（langtu.md §1.2）。</summary>
    private const byte InvalidSentinel = 0xFF;

    /// <summary>
    /// 解析一帧；false = 不是本协议帧或读数无效。
    /// 同步头允许出现在下标 0 或 1：多数读路径带 report ID 0，个别路径会把它剥掉。
    /// </summary>
    public static bool TryParse(byte[] input, out int percent)
    {
        percent = 0;

        int sync = input.Length > 1 && input[0] == Sync0 && input[1] == Sync1 ? 0
            : input.Length > 2 && input[1] == Sync0 && input[2] == Sync1 ? 1
            : -1;
        if (sync < 0) return false;
        if (input.Length < sync + PercentOffset + 1) return false;
        if (input[sync + CmdOffset] != Cmd) return false;

        byte raw = input[sync + PercentOffset];
        if (raw == InvalidSentinel || raw > 100) return false;

        percent = raw;
        return true;
    }
}
