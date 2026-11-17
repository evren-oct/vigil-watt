namespace VigilWatt.Core.Plugins.Flydigi;

/// <summary>
/// 功 能： 飞智 NewXInput 心跳帧的构造与解析（APEX5 与 APEX6 两代 2.4G 接收器）。
/// 2026/10/22 21:50:00 xliu
/// 2026/11/17 meish 扩展 APEX6（0x2502）：按 [5] 型号 ID 分档，见 docs/hardware/brands/flydigi.md §2.2。
/// </summary>
public static class FlydigiCodec
{
    public const ushort VendorId = 0x37D7;
    public const ushort Apex5DonglePid = 0x2501;
    public const ushort Apex6DonglePid = 0x2502;

    /// <summary>厂商协议集合；同一 dongle 上另有一个 0xFFEE 集合不承载电量。</summary>
    public const ushort VendorUsagePage = 0xFFA0;
    public const ushort VendorUsage = 0x0001;

    /// <summary>APEX5 含 report ID 的整帧长度（贡献者实测 32/32）。</summary>
    public const int HidReportLength = 32;

    /// <summary>APEX6 本机实测 33/33（比 APEX5 多一字节）；两代长度不同，判据必须按 PID 分开。</summary>
    public const int Apex6HidReportLength = 33;

    /// <summary>caps 读不到输出 report ID 时的兜底值（实测接口就是 0x03）。</summary>
    public const byte FallbackWriteReportId = 0x03;

    public const byte Prefix0 = 0x5A;
    public const byte Prefix1 = 0xA5;
    public const byte HeartbeatCmd = 0x01;

    /// <summary>档位上限：0–5 共 6 档，×20 = 百分比（APEX5）。</summary>
    public const byte MaxLevel = 5;

    /// <summary>APEX6 家族在 payload[5] 里的型号 ID（SDL 的 149 Apex6 / 150 Apex6 Pro / 152 幻影版）。</summary>
    public const byte DeviceIdApex6 = 149;
    public const byte DeviceIdApex6Pro = 150;
    public const byte DeviceIdApex6ProPhantom = 152;

    /// <summary>插线后状态字节整体变成 0x11，此时**不含电量**（APEX5，见文档 §4）。</summary>
    public const byte ChargingCode = 0x11;

    /// <summary>状态字节相对帧头 0x5A 的偏移（整帧绝对下标 12）。</summary>
    private const int StatusOffset = 11;

    /// <summary>型号 ID 字节相对帧头的偏移。</summary>
    private const int DeviceIdOffset = 5;

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
    {
        int expectedLength = productId == Apex6DonglePid ? Apex6HidReportLength : HidReportLength;
        return vendorId == VendorId
               && (productId == Apex5DonglePid || productId == Apex6DonglePid)
               && usagePage == VendorUsagePage && usage == VendorUsage
               && inputLength == expectedLength && outputLength == expectedLength;
    }

    /// <summary>
    /// 解析心跳应答。返回 false = 不是本协议帧（队列里混着摇杆/按键报文与 cmd 0x52 的其它帧）。
    /// 两代按 [5] 型号 ID 分流：149/150/152 = APEX6（0~10 档 ×10），其余走 APEX5（0~5 档 ×20）。
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> report, out Status status)
    {
        status = default;

        // 帧头前可能有 report ID（读 input report 时由驱动前置），只在最前面几个字节里找
        int start = FindPrefix(report);
        if (start < 0 || report.Length < start + StatusOffset + 1) return false;
        if (report[start + 2] != HeartbeatCmd) return false;

        byte deviceId = report[start + DeviceIdOffset];
        byte level = report[start + StatusOffset];

        // APEX6：状态字节高 4 位充电状态 / 低 4 位档位（0~10，×10），已充满直接 100%
        if (IsApex6Family(deviceId))
            return TryParseApex6(level, out status);

        // APEX5：充电时设备不再上报档位，而是把这个字节整体换成 0x11。
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

    /// <summary>APEX6 的 [11]：高 4 位 0 放电 / 1 充电中 / 2 已充满；低 4 位 0~10 档。已充满直接给 100%。</summary>
    private static bool TryParseApex6(byte level, out Status status)
    {
        status = default;
        byte charging = (byte)((level >> 4) & 0x0F);
        byte bucket = (byte)(level & 0x0F);

        // 高 4 位只有 0/1/2 有定义，其余是未就绪或非本协议帧
        if (charging > 2) return false;

        // 已充满：直接 100%，档位残留 0x0A（10/10）也按满处理
        if (charging == 2)
        {
            status = new Status(100, level, false);
            return true;
        }

        // 0~10 档 ×10；越界说明协议变了，宁可当没读到
        if (bucket > 10) return false;

        status = new Status(bucket * 10, level, charging == 1);
        return true;
    }

    /// <summary>型号 ID 是否属于 APEX6 家族（149 / 150 Apex6 Pro / 152 幻影版）。</summary>
    private static bool IsApex6Family(byte deviceId)
        => deviceId is DeviceIdApex6 or DeviceIdApex6Pro or DeviceIdApex6ProPhantom;

    private static int FindPrefix(ReadOnlySpan<byte> report)
    {
        int limit = Math.Min(3, report.Length - 1);
        for (int i = 0; i < limit; i++)
            if (report[i] == Prefix0 && report[i + 1] == Prefix1) return i;
        return -1;
    }
}
