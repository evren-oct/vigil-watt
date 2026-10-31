namespace VigilWatt.Core.Plugins.Betop;

/// <summary>
/// 功 能： 北通 (BETOP) 手柄状态报告的纯帧编解码——不碰句柄、不写日志，便于单测。
/// 2026/10/06 15:53:36 xliu
/// 帧结构、T9 被动流、占位帧判定与实测依据见 docs/hardware/betop.md §1。
/// </summary>
internal static class BetopCodec
{
    /// <summary>状态报告 report ID（鲲鹏20 = 0x02；协议家族一致，型号间可能不同）。</summary>
    public const byte ReportId = 0x02;

    public const byte CmdStatus = 0x05;

    public const byte SubStatus = 0x01;

    /// <summary>header 字节 = (subcmd &lt;&lt; 4) | cmd = 0x15。</summary>
    public const byte StatusHeader = (SubStatus << 4) | CmdStatus;

    /// <summary>整帧偏移：电量百分比（0~100）。</summary>
    public const int BatteryOffset = 2;

    /// <summary>整帧偏移：充电标志所在字节（取低 4 位）。</summary>
    public const int ChargeOffset = 4;

    /// <summary>充电标志掩码（低 4 位，非 0 = 充电中）。</summary>
    public const byte ChargeMask = 0x0F;

    /// <summary>无效电量下界：0 视为设备未就绪/占位值，不接受。</summary>
    public const byte InvalidPercentFloor = 0;

    // T9 被动状态流（Col04 持续推流，不响应查询）。识别特征只能取 report ID：
    // byte[1] 是可变状态位（实测见过 0x02/0x04/0x0C），拿它当帧头会让解析全数落空。

    /// <summary>T9 状态流所在 collection 的 report ID（整帧偏移 0；无 ID 前缀的布局在偏移 1）。</summary>
    public const byte T9StreamReportId = 0x04;

    /// <summary>T9 状态流帧最小长度（含 report ID）。</summary>
    public const int T9MinLength = 24;

    /// <summary>T9 电量原始字节所在偏移。</summary>
    public const int T9BatteryOffset = 23;

    /// <summary>T9 电量满值：该字节编码为"4-bit 电量等级(0~15) × 8"，故满值 = 120。</summary>
    public const byte T9BatteryFull = 120;

    /// <summary>
    /// dongle 在"手柄未连接（关机/休眠/未配对）"时上报的占位值（raw 80）——不是电量。
    /// 开机态永远是 0x58，故只认 0x50 是零风险判据。详见 betop.md §1.4.1。
    /// </summary>
    public const byte T9NotLinkedRaw = 0x50;

    /// <summary>
    /// 占位帧 ②：dongle 在位、手柄**根本没开机**时推的整帧全 0 空帧——byte[1]=0x00、摇杆位全 0、
    /// byte23=0x10。0x10 按"等级×8"读是 13%，会被当低电量误报。见 betop.md §1.4.2。
    /// </summary>
    public const byte T9BlankStateByte = 0x00;

    /// <summary>空帧的 byte23（= 等级 2 × 8）。**单独出现不是判据**，须与 <see cref="T9BlankStateByte"/> 同时成立。</summary>
    public const byte T9BlankRaw = 0x10;

    /// <summary>把 T9 原始电量字节（0~120）换算成百分比（0~100，四舍五入）。</summary>
    public static int T9ToPercent(byte raw) => (raw * 100 + T9BatteryFull / 2) / T9BatteryFull;

    // 北通有两代 dongle、两个 usage page，判据收在这里便于单测（接口地图见 betop.md §1.1 §1.8）。

    /// <summary>旧接收器的 VID（T9、鲲鹏20 老批次）。</summary>
    public const ushort VendorId = 0x20BC;

    /// <summary>新批次 dongle 的 VID（`KP20 DONGLE`、`BTP-KP70A DONGLE` 底座）。</summary>
    public const ushort VendorIdNew = 0x20DD;

    /// <summary>厂商集合的 usage page（T9 / 老批次，须查询才回帧）。</summary>
    public const ushort VendorPage = 0xFF00;

    /// <summary>杂项广播集合的 usage page（鲲鹏20/70 的 dongle 每秒自推电量）。</summary>
    public const ushort BroadcastPage = 0x00FF;

    /// <summary>北通 VID：`0x20BC` 旧接收器、`0x20DD` 新批次 dongle。</summary>
    public static bool IsBetopVendor(ushort vendorId) => vendorId is VendorId or VendorIdNew;

    /// <summary>
    /// 该集合是不是北通的电量通道：VID 命中，且 page/usage 含厂商页或广播页（两种集合的 usage 都是 `0x0003`）。
    /// usage 侧也查一遍以兼容扫描器把 page/usage 读颠倒；标准 VR 页 `0x0003` 因此不再单独命中。
    /// </summary>
    public static bool IsVendorInterface(ushort vendorId, ushort usagePage, ushort usage)
        => IsBetopVendor(vendorId) && (IsVendorPage(usagePage) || IsVendorPage(usage));

    /// <summary>杂项广播页（`0x00FF`）承载鲲鹏20/70 的每秒自推：探测与轮询先纯听；厂商页仍直接查询。</summary>
    public static bool IsBroadcastInterface(ushort vendorId, ushort usagePage, ushort usage)
        => IsVendorInterface(vendorId, usagePage, usage)
           && (usagePage == BroadcastPage || usage == BroadcastPage);

    private static bool IsVendorPage(ushort value) => value is VendorPage or BroadcastPage;

    /// <summary>T9 被动状态流的解析结果。</summary>
    /// <param name="Percent">电量百分比（0~100）；<paramref name="Linked"/> 为 false 时无意义（是占位值）。</param>
    /// <param name="Charging">是否充电中（T9 流无充电标志字节，恒为 false）。</param>
    /// <param name="Linked">false = dongle 上报"未连接"占位帧，手柄并不在线，调用方应标离线。</param>
    public readonly record struct T9Status(int Percent, bool Charging, bool Linked);

    /// <summary>
    /// 解析 T9 的被动状态流帧；非该帧型或电量越界返回 null。
    /// 识别依据只有 report ID，不要按 byte[1] 判帧头。
    /// </summary>
    public static T9Status? ParseT9Status(byte[] input)
    {
        foreach (int off in new[] { 0, 1 })
        {
            if (input.Length < off + T9MinLength) continue;
            if (input[off] != T9StreamReportId) continue;

            byte raw = input[off + T9BatteryOffset];
            if (raw <= InvalidPercentFloor || raw > T9BatteryFull) return null;

            // 占位帧：手柄没连上，不是电量。
            if (raw == T9NotLinkedRaw) return new T9Status(0, false, Linked: false);

            // 占位帧 ②（关机空帧）：byte[1] 是组件状态位，0x00 = 一个都没有。真机 241 帧里
            // byte[1] ∈ {0x02, 0x03}，从未出现 0x00；两个条件同时成立才判，避免误伤真实的 13%。
            if (raw == T9BlankRaw && input[off + 1] == T9BlankStateByte)
                return new T9Status(0, false, Linked: false);

            return new T9Status(T9ToPercent(raw), false, Linked: true);
        }
        return null;
    }

    /// <summary>
    /// 把 Windows 的 USB product string 收拾成型号名：去厂商前缀与 "For …" 用途后缀。
    /// 例：<c>"BEITONG T9 For Keyboard &amp; Mouse"</c> → <c>"T9"</c>（不去掉的话 UI 会按
    /// 名字里的 "Keyboard" 把它画成键盘图标）。
    /// </summary>
    public static string CleanProductName(string? product)
    {
        if (string.IsNullOrWhiteSpace(product)) return "";
        var s = product.Trim();

        foreach (var vendor in new[] { "BEITONG", "BETOP", "北通" })
        {
            if (s.StartsWith(vendor, StringComparison.OrdinalIgnoreCase))
            {
                s = s[vendor.Length..].TrimStart(' ', '-', '_', ':', '：');
                break;
            }
        }

        int forIdx = s.IndexOf(" For ", StringComparison.OrdinalIgnoreCase);
        if (forIdx > 0) s = s[..forIdx];

        return s.Trim();
    }

    public static byte EncodeHeader(byte cmd, byte subcmd)
        => (byte)(((subcmd & 0x0F) << 4) | (cmd & 0x0F));

    public static (byte Cmd, byte Subcmd) DecodeHeader(byte header)
        => ((byte)(header & 0x0F), (byte)((header >> 4) & 0x0F));

    /// <summary>
    /// 构造一条查询帧（不足部分补零）。北通接收器对写入长度敏感，必须写满接口声明的输出
    /// 报文长度，故长度由调用方按接口给出。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> 小于 2。</exception>
    public static byte[] BuildQuery(int length, byte reportId = ReportId, byte header = StatusHeader)
    {
        if (length < 2)
            throw new ArgumentOutOfRangeException(nameof(length), "长度至少 2（report_id + header）");
        var frame = new byte[length];
        frame[0] = reportId;
        frame[1] = header;
        return frame;
    }

    /// <summary>接口未声明 report ID（描述符无 0x85 项）时：header 直接放报文首字节，无 ID 前缀。</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> 小于 1。</exception>
    public static byte[] BuildQueryWithoutReportId(int length, byte header = StatusHeader)
    {
        if (length < 1)
            throw new ArgumentOutOfRangeException(nameof(length), "长度至少 1（header）");
        var frame = new byte[length];
        frame[0] = header;
        return frame;
    }

    /// <summary>
    /// 解析状态报告帧（header 0x15），返回 (电量%, 是否充电中)；非状态报告或电量无效返回 null。
    /// Windows 的 ReadInput 缓冲带 report ID 前缀（下标 0），这里同时兼容无前缀布局（下标 1）。
    /// </summary>
    public static (int Percent, bool Charging)? ParseStatus(byte[] input)
    {
        foreach (int off in new[] { 0, 1 })
        {
            if (input.Length < off + ChargeOffset + 1) continue;
            if (input[off] != ReportId || input[off + 1] != StatusHeader) continue;

            byte pct = input[off + BatteryOffset];
            if (pct <= InvalidPercentFloor || pct > 100) return null;
            bool charging = (input[off + ChargeOffset] & ChargeMask) != 0;
            return (pct, charging);
        }
        return null;
    }
}
