namespace VigilWatt.Core.Plugins.Flydigi;

/// <summary>
/// 功 能： 飞智 NewXInput 心跳帧的构造与解析（APEX5 2.4G 接收器）。
/// 2026/10/22 21:50:00 xliu
/// 字节表、证据链与未验证项见 docs/hardware/flydigi.md。
/// </summary>
public static class FlydigiCodec
{
    public const ushort VendorId = 0x37D7;
    public const ushort Apex5DonglePid = 0x2501;

    /// <summary>厂商协议集合；同一 dongle 上另有一个 0xFFEE 集合不承载电量。</summary>
    public const ushort VendorUsagePage = 0xFFA0;
    public const ushort VendorUsage = 0x0001;

    /// <summary>含 report ID 的整帧长度。</summary>
    public const int HidReportLength = 32;

    /// <summary>caps 读不到输出 report ID 时的兜底值（实测接口就是 0x03）。</summary>
    public const byte FallbackWriteReportId = 0x03;

    public const byte Prefix0 = 0x5A;
    public const byte Prefix1 = 0xA5;
    public const byte HeartbeatCmd = 0x01;

    /// <summary>档位上限：0–5 共 6 档，×20 = 百分比。</summary>
    public const byte MaxLevel = 5;

    /// <summary>插线后状态字节整体变成 0x11，此时**不含电量**（见文档 §4）。</summary>
    public const byte ChargingCode = 0x11;

    /// <summary>状态字节相对帧头 0x5A 的偏移（整帧绝对下标 12）。</summary>
    private const int StatusOffset = 11;

    /// <summary>一条心跳应答的解析结果；<paramref name="Percent"/> 为 null 表示"充电中，电量未知"。</summary>
    public readonly record struct Status(int? Percent, byte RawLevel, bool IsCharging);

    /// <summary>构造心跳请求。<paramref name="reportId"/> 取 caps 的值，长度取输出报文长度。</summary>
    public static byte[] CreateHeartbeat(byte reportId, int length)
    {
        var frame = new byte[Math.Max(length, HidReportLength)];
        frame[0] = reportId;
        frame[1] = Prefix0;
        frame[2] = Prefix1;
        frame[3] = HeartbeatCmd;
        frame[4] = 0x02;
        frame[5] = 0x03;
        return frame;
    }

    public static bool IsSupportedInterface(ushort vendorId, ushort productId, ushort usagePage, ushort usage,
        ushort inputLength, ushort outputLength)
        => vendorId == VendorId && productId == Apex5DonglePid
           && usagePage == VendorUsagePage && usage == VendorUsage
           && inputLength == HidReportLength && outputLength == HidReportLength;

    /// <summary>
    /// 解析心跳应答。返回 false = 不是本协议帧（队列里混着摇杆/按键报文与 cmd 0x52 的其它帧）。
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> report, out Status status)
    {
        status = default;

        // 帧头前可能有 report ID（读 input report 时由驱动前置），只在最前面几个字节里找
        int start = FindPrefix(report);
        if (start < 0 || report.Length < start + StatusOffset + 1) return false;
        if (report[start + 2] != HeartbeatCmd) return false;

        byte level = report[start + StatusOffset];

        // 充电时设备不再上报档位，而是把这个字节整体换成 0x11。
        // 按档位算会得到 17×20=340%，按低 4 位算会得到 20%（实际是 40%）——两种都错。
        if (level == ChargingCode)
        {
            status = new Status(null, level, true);
            return true;
        }

        // 越界说明不是心跳应答（或协议变了），宁可当没读到也不编一个电量出来
        if (level > MaxLevel) return false;

        status = new Status(level * 20, level, false);
        return true;
    }

    private static int FindPrefix(ReadOnlySpan<byte> report)
    {
        int limit = Math.Min(3, report.Length - 1);
        for (int i = 0; i < limit; i++)
            if (report[i] == Prefix0 && report[i + 1] == Prefix1) return i;
        return -1;
    }
}
