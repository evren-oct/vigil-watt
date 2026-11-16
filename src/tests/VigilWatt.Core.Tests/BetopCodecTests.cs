using VigilWatt.Core.Plugins.Betop;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： BetopCodec —— 北通 (BETOP) 手柄状态帧编解码与电量通道 usage page 判定。
/// 2026/10/06 15:53:36 xliu
/// 帧结构见 docs/hardware/brands/betop.md §1。
/// </summary>
public class BetopCodecTests
{
    private static byte[] Hex(string s) => Convert.FromHexString(s.Replace(" ", ""));

    /// <summary>充电中（官方实测帧）：100% + 充电标志低 4 位 = 1。</summary>
    private static readonly byte[] ChargingFrame =
        Hex("02 15 64 00 51 01 01 00 00 00 00 00 00 00 00 00");

    /// <summary>用电池（官方实测帧）：0x5D = 93%，充电标志低 4 位 = 0。</summary>
    private static readonly byte[] DischargingFrame =
        Hex("02 15 5D 00 50 01 01 00 00 00 00 00 00 00 00 00");

    [Fact]
    public void C1_Header打包与拆包()
    {
        Assert.Equal((byte)0x15, BetopCodec.EncodeHeader(BetopCodec.CmdStatus, BetopCodec.SubStatus));
        var (cmd, subcmd) = BetopCodec.DecodeHeader(BetopCodec.StatusHeader);
        Assert.Equal(BetopCodec.CmdStatus, cmd);
        Assert.Equal(BetopCodec.SubStatus, subcmd);
    }

    [Fact]
    public void C2_构造查询帧与官方一致()
    {
        byte[] built = BetopCodec.BuildQuery(16);
        Assert.Equal(16, built.Length);
        Assert.Equal("0215" + new string('0', 28), Convert.ToHexString(built));
    }

    [Fact]
    public void C3_构造查询帧长度不足2应报错()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BetopCodec.BuildQuery(1));
    }

    [Fact]
    public void C4_解析充电帧_100且充电中()
    {
        var parsed = BetopCodec.ParseStatus(ChargingFrame);
        Assert.NotNull(parsed);
        Assert.Equal(100, parsed!.Value.Percent);
        Assert.True(parsed.Value.Charging);
    }

    [Fact]
    public void C5_解析放电帧_93且未充电()
    {
        var parsed = BetopCodec.ParseStatus(DischargingFrame);
        Assert.NotNull(parsed);
        Assert.Equal(93, parsed!.Value.Percent);
        Assert.False(parsed.Value.Charging);
    }

    [Fact]
    public void C6_非状态报告帧不误判()
    {
        // header 0x25 = 按键/摇杆报告，不是电量
        Assert.Null(BetopCodec.ParseStatus(Hex("02 25 42 00 01 00 00 00")));
        // report_id 不对
        Assert.Null(BetopCodec.ParseStatus(Hex("13 15 42 00 01 00 00 00")));
    }

    [Fact]
    public void C7_电量越界或占位值不采用()
    {
        Assert.Null(BetopCodec.ParseStatus(Hex("02 15 00 00 50 00 00 00")));   // 0 = 未就绪/占位
        Assert.Null(BetopCodec.ParseStatus(Hex("02 15 65 00 50 00 00 00")));   // 0x65=101 越界
    }

    [Fact]
    public void C8_长度不足不崩溃()
    {
        Assert.Null(BetopCodec.ParseStatus(Hex("02 15")));
        Assert.Null(BetopCodec.ParseStatus(Array.Empty<byte>()));
    }

    [Fact]
    public void C9_兼容无reportId前缀布局_整体右移一字节()
    {
        byte[] shifted = new byte[DischargingFrame.Length + 1];
        Array.Copy(DischargingFrame, 0, shifted, 1, DischargingFrame.Length);

        var parsed = BetopCodec.ParseStatus(shifted);
        Assert.NotNull(parsed);
        Assert.Equal(93, parsed!.Value.Percent);
        Assert.False(parsed.Value.Charging);
    }

    [Fact]
    public void C10_充电标志只看低4位()
    {
        // byte[4]=0xF0 → 低 4 位为 0 = 未充电（高 4 位是保留位，不能误判成充电）
        Assert.False(BetopCodec.ParseStatus(Hex("02 15 5D 00 F0 00 00 00"))!.Value.Charging);
        // byte[4]=0x0F → 低 4 位非 0 = 充电
        Assert.True(BetopCodec.ParseStatus(Hex("02 15 5D 00 0F 00 00 00"))!.Value.Charging);
    }

    // ---------------------------------------------------------------- T9 被动状态流
    // 真机帧（2026-10-06 / 10-29 / 10-30 / 10-31，USB\VID_20BC&PID_5073 的 Col04；dongle 持续推、不响应查询）。
    // 电量口径按官方客户端 BetopGame 2.6.2：raw[1] 低 3 位 = 电量等级，映射 [0,50,80,100,100]；
    // raw[23] 是连接/模式标志字节，**不是电量**。见 docs/hardware/reference/betop-official-client.md §4.2。

    /// <summary>按 T9 状态流布局造一帧：只填识别位与两个关键字节，其余补 0。</summary>
    private static byte[] T9Frame(byte raw1, byte raw23)
    {
        var frame = new byte[24];
        frame[0] = BetopCodec.T9StreamReportId;
        frame[1] = raw1;
        frame[23] = raw23;
        return frame;
    }

    /// <summary>手柄唤醒后的状态流帧：raw[1] = 0x02 → 等级 2 → 80%。</summary>
    private static readonly byte[] T9StreamWake =
        Hex("04 02 80 80 80 80 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 58");

    /// <summary>同一只 T9 另一段真机帧：raw[1] = 0x03 → 等级 3 → 100%（2026-10-29 19:25 起，共 43 帧）。</summary>
    private static readonly byte[] T9StreamLevel3 =
        Hex("04 03 80 80 80 80 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 58");

    /// <summary>
    /// 手柄**没连上**时 dongle 推的占位帧：raw[23] = 0x50。该值不是电量。
    /// 真机对照见 docs/hardware/brands/betop.md §1.4.1。
    /// </summary>
    private static readonly byte[] T9StreamNotLinked =
        Hex("04 02 80 80 80 80 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 50");

    /// <summary>旧实现按"等级×8"读成 100%、据此判过"满电"的那帧：raw[23] = 0x78。</summary>
    private static readonly byte[] T9StreamFull =
        Hex("04 02 80 80 80 80 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 78");

    /// <summary>
    /// 同一天下午的真机帧：**raw[1] 变成 0x0C**（上午是 0x02），其余完全一致 → 等级 4 → 100%。
    /// 旧实现把 raw[1] 当帧头 → 这帧解析全落空 → 手柄从列表消失。
    /// </summary>
    private static readonly byte[] T9StreamWakeTag0C =
        Hex("04 0C 80 80 80 80 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 58");

    /// <summary>同上，占位值 0x50（同一批 81 条真机帧里 raw[1] ∈ {0x02, 0x0C}）。</summary>
    private static readonly byte[] T9StreamNotLinkedTag0C =
        Hex("04 0C 80 80 80 80 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 50");

    /// <summary>
    /// **用户报"我设备已经关闭了 只有连接器还在电脑上"那一刻的真机帧**（2026-10-06 17:03 起）：
    /// raw[1] = 0x04、raw[23] = 0x50。
    /// </summary>
    private static readonly byte[] T9StreamNotLinkedTag04 =
        Hex("04 04 80 80 80 80 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 50");

    /// <summary>
    /// **用户报"我北通的手柄并没有连接，为什么会显示低电量"那一刻的真机帧**（2026-10-30 19:37、
    /// 2026-10-31 10:51，两次都是开机时手柄没开）：整帧全 0，只有 raw[23] = 0x10。
    /// </summary>
    private static readonly byte[] T9StreamBlank =
        Hex("04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 10");

    [Fact]
    public void T1_解析T9状态流_等级2_80()
    {
        var parsed = BetopCodec.ParseT9Status(T9StreamWake);
        Assert.NotNull(parsed);
        Assert.Equal(80, parsed!.Value.Percent);
        Assert.False(parsed.Value.Charging);
        Assert.True(parsed.Value.Linked);
    }

    [Fact]
    public void T1b_解析T9状态流_等级3_100()
    {
        var parsed = BetopCodec.ParseT9Status(T9StreamLevel3);
        Assert.Equal(100, parsed!.Value.Percent);
        Assert.True(parsed.Value.Linked);
    }

    /// <summary>官方位域映射全表：raw[1] &amp; 0x07 → 百分比。</summary>
    [Theory]
    [InlineData(0x00, 0)]
    [InlineData(0x01, 50)]
    [InlineData(0x02, 80)]
    [InlineData(0x03, 100)]
    [InlineData(0x04, 100)]
    public void T14_官方位域映射_逐级核对(byte raw1, int expected)
    {
        var parsed = BetopCodec.ParseT9Status(T9Frame(raw1, raw23: 0x58));
        Assert.NotNull(parsed);
        Assert.True(parsed!.Value.Linked);
        Assert.Equal(expected, parsed.Value.Percent);
    }

    /// <summary>表外等级（5~7）必须是 Unknown：帧有效、手柄在线，但没有可信电量。</summary>
    [Theory]
    [InlineData(0x05)]
    [InlineData(0x06)]
    [InlineData(0x07)]
    public void T6_等级表外为Unknown(byte raw1)
    {
        var parsed = BetopCodec.ParseT9Status(T9Frame(raw1, raw23: 0x58));
        Assert.NotNull(parsed);
        Assert.True(parsed!.Value.Linked);
        Assert.Null(parsed.Value.Percent);
    }

    /// <summary>电量只取低 3 位：高位是标志位（0x08 为链路位），不影响等级。</summary>
    [Theory]
    [InlineData(0x08, 0)]
    [InlineData(0x09, 50)]
    [InlineData(0x0A, 80)]
    [InlineData(0x0B, 100)]
    [InlineData(0x0C, 100)]
    public void T15_电量只取低3位(byte raw1, int expected)
    {
        var parsed = BetopCodec.ParseT9Status(T9Frame(raw1, raw23: 0x58));
        Assert.Equal(expected, parsed!.Value.Percent);
    }

    /// <summary>
    /// raw[23] 是状态标志字节：真机非空帧时它是 0x58，与电量无关。
    /// 这里逐个换值，电量必须始终由 raw[1] 决定（0x50 是占位值，单列在 T2）。
    /// </summary>
    [Theory]
    [InlineData(0x10)]
    [InlineData(0x18)]
    [InlineData(0x58)]
    [InlineData(0x78)]
    [InlineData(0x7F)]
    public void T10_raw23不参与电量(byte raw23)
    {
        var parsed = BetopCodec.ParseT9Status(T9Frame(raw1: 0x02, raw23: raw23));
        Assert.NotNull(parsed);
        Assert.Equal(80, parsed!.Value.Percent);
    }

    [Fact]
    public void T7_byte1是可变状态位_0x0C也要认()
    {
        // 回归：raw[1] 不是固定帧头，而是"电量等级 + 标志位"的打包字节
        var wake = BetopCodec.ParseT9Status(T9StreamWakeTag0C);
        Assert.Equal(100, wake!.Value.Percent);
        Assert.True(wake.Value.Linked);

        // 0x0C 下的占位帧同样要认出来是"没连上"（不能因为 raw[1] 变了就当正常读数）
        var notLinked = BetopCodec.ParseT9Status(T9StreamNotLinkedTag0C);
        Assert.NotNull(notLinked);
        Assert.False(notLinked!.Value.Linked);
    }

    [Fact]
    public void T8_无reportId前缀布局下byte1为0x0C也能认()
    {
        // 整体右移一字节（[0] 是 0x00 占位）时，report ID 落在偏移 1
        byte[] shifted = new byte[T9StreamWakeTag0C.Length + 1];
        Array.Copy(T9StreamWakeTag0C, 0, shifted, 1, T9StreamWakeTag0C.Length);
        Assert.Equal(100, BetopCodec.ParseT9Status(shifted)!.Value.Percent);
    }

    [Fact]
    public void T2_解析T9占位帧_手柄未连接_不算电量()
    {
        // ⚠ 回归（2026-10-06 晚用户复报）：dongle 插着、手柄关了，dongle 仍推 raw[23]=0x50 的占位帧。
        // 旧实现把它当 67% 采信 → 列表里"北通 T9 67% 在线"。现在必须标成"未连接"。
        var parsed = BetopCodec.ParseT9Status(T9StreamNotLinked);
        Assert.NotNull(parsed);
        Assert.False(parsed!.Value.Linked);
        Assert.Null(parsed.Value.Percent);
    }

    [Fact]
    public void T9_手柄关机真机帧_0x04加0x50也算未连接()
    {
        // 用户报 bug 当下的原帧（raw[1] = 0x04，与休眠期的 0x02 又不同）
        var parsed = BetopCodec.ParseT9Status(T9StreamNotLinkedTag04);
        Assert.NotNull(parsed);
        Assert.False(parsed!.Value.Linked);
    }

    [Fact]
    public void T11_关机空帧_整帧全0且byte23为0x10_不算0()
    {
        // ⚠ 回归（2026-10-31 用户复报）：dongle 插着、手柄根本没开机时推的是**全 0 空帧**。
        // 空帧的等级字节也是 0x00，若不先判占位帧就会被读成"真实 0%"。
        var parsed = BetopCodec.ParseT9Status(T9StreamBlank);
        Assert.NotNull(parsed);
        Assert.False(parsed!.Value.Linked);
        Assert.Null(parsed.Value.Percent);
    }

    [Fact]
    public void T12_空帧判据不误伤真实读数_byte1非0时照旧采信()
    {
        // 空帧与正常帧只差等级字节：开机帧是 0x02/0x03，空帧是 0x00 + raw[23]=0x10。
        byte[] real = (byte[])T9StreamBlank.Clone();
        real[1] = 0x02;
        var parsed = BetopCodec.ParseT9Status(real);
        Assert.NotNull(parsed);
        Assert.True(parsed!.Value.Linked);
        Assert.Equal(80, parsed.Value.Percent);
    }

    [Fact]
    public void T13_无reportId前缀布局下空帧也能认()
    {
        byte[] shifted = new byte[T9StreamBlank.Length + 1];
        Array.Copy(T9StreamBlank, 0, shifted, 1, T9StreamBlank.Length);
        Assert.False(BetopCodec.ParseT9Status(shifted)!.Value.Linked);
    }

    [Fact]
    public void T5_旧口径的满值帧_新口径按等级读为80()
    {
        // 旧实现把 raw[23]=0x78 当"等级 15 × 8"读成 100%，据此判过"满电"。
        // 官方口径里 raw[23] 只是标志字节，电量只看 raw[1]=0x02 → 80%。
        var parsed = BetopCodec.ParseT9Status(T9StreamFull);
        Assert.Equal(80, parsed!.Value.Percent);
        Assert.True(parsed.Value.Linked);
    }

    [Fact]
    public void T4_状态字节任意值都不再越界丢弃()
    {
        // 旧实现拿 raw[23] 当电量并做 0~120 越界过滤，0x7F / 0x00 会被整帧丢掉。
        // 现在 raw[23] 只是标志字节，越界与否不影响解析。
        Assert.Equal(80, BetopCodec.ParseT9Status(
            Hex("04 02 80 80 80 80 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 7F"))!.Value.Percent);
        Assert.Equal(80, BetopCodec.ParseT9Status(
            Hex("04 02 80 80 80 80 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"))!.Value.Percent);
    }

    [Fact]
    public void T3_T9解析不误判其它帧()
    {
        // Col05 的摇杆流：report ID 是 0x05，不是 0x04
        Assert.Null(BetopCodec.ParseT9Status(
            Hex("05 60 fd e7 fe 1b 0f 27 00 d9 ff f0 ff 01 06 00 80 80 80 80 00 00 00 00")));
        // Col05 的 byte[1] 恰好是 0x04 也不行——24 字节长度下偏移 1 的窗口越界，不会被当成 report ID
        Assert.Null(BetopCodec.ParseT9Status(
            Hex("05 04 fd e7 fe 1b 0f 27 00 d9 ff f0 ff 01 06 00 80 80 80 80 00 00 00 58")));
        // Col01 的配置流（01 0b …）
        Assert.Null(BetopCodec.ParseT9Status(
            Hex("01 0b d5 31 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00")));
        // 5075 接收器的全零 64 字节帧
        Assert.Null(BetopCodec.ParseT9Status(new byte[64]));
        // 长度不足
        Assert.Null(BetopCodec.ParseT9Status(Hex("04 02 80 80 80")));
        Assert.Null(BetopCodec.ParseT9Status(Array.Empty<byte>()));
    }

    // ---------------------------------------------------------------- 电量接口判据（两代 VID × 两个页）
    // 20DD 新批次 dongle 的判据见 docs/hardware/brands/betop.md §1.8；旧判据把标准 VR 页 0x0003 也算广播页。

    [Theory]
    [InlineData(0x20BC, 0xFF00, 0x0003, true)]    // 旧接收器（T9 / 鲲鹏20 老批次）
    [InlineData(0x20BC, 0x00FF, 0x0003, true)]    // 直插的 BTP-KP70A XINPUT DONGLE
    [InlineData(0x20DD, 0x00FF, 0x0003, true)]    // 底座接收器、KP20 DONGLE
    [InlineData(0x20DD, 0xFF00, 0x0003, true)]    // 新 VID + 旧页，一并收
    [InlineData(0x20BC, 0x0003, 0x00FF, true)]    // 扫描器把 page/usage 读颠倒
    [InlineData(0x20BC, 0x0001, 0x0005, false)]   // 手柄本体（XInput 呈现）
    [InlineData(0x20DD, 0x0001, 0x0006, false)]   // 键盘模拟接口
    [InlineData(0x20BC, 0x0003, 0x0003, false)]   // 标准 VR 页：旧判据会误命中，现已收紧
    [InlineData(0x1532, 0x00FF, 0x0003, false)]   // 别家的 0x00FF 厂商口
    [InlineData(0x3837, 0xFF00, 0x0003, false)]   // 迈从 dongle 的厂商口
    public void B1_电量接口判据(ushort vid, ushort usagePage, ushort usage, bool expected)
        => Assert.Equal(expected, BetopCodec.IsVendorInterface(vid, usagePage, usage));

    [Fact]
    public void B1b_只有广播页走先纯听()
    {
        // 0x00FF：dongle 每秒自推，探测/轮询先纯听（零写入）
        Assert.True(BetopCodec.IsBroadcastInterface(0x20BC, 0x00FF, 0x0003));
        Assert.True(BetopCodec.IsBroadcastInterface(0x20DD, 0x00FF, 0x0003));
        Assert.True(BetopCodec.IsBroadcastInterface(0x20BC, 0x0003, 0x00FF));    // page/usage 读颠倒
        // 厂商页 0xFF00（T9 三个接口）：T9 不答查询但仍直接写，别为它白等 3 秒纯听
        Assert.False(BetopCodec.IsBroadcastInterface(0x20BC, 0xFF00, 0x0003));
        Assert.False(BetopCodec.IsBroadcastInterface(0x20DD, 0xFF00, 0x0003));
        // 手柄接口本来就不是候选
        Assert.False(BetopCodec.IsBroadcastInterface(0x20BC, 0x0001, 0x0005));
    }

    [Fact]
    public void B2_广播帧与查询响应同构_现有解析直接认()
    {
        // 真机广播帧（2026-10-25 偷听）：41% 未充电，每秒一帧
        var parsed = BetopCodec.ParseStatus(Hex("02 15 29 00 50 01 01 00 00 00 00 00 00 00 00 00"));
        Assert.NotNull(parsed);
        Assert.Equal(41, parsed!.Value.Percent);
        Assert.False(parsed.Value.Charging);
    }

    // ---------------------------------------------------------------- 产品串收拾成型号名

    [Fact]
    public void P1_T9真机产品串收拾成型号()
    {
        // 真机 USB product string：带厂商前缀 + "For …" 用途说明
        Assert.Equal("T9", BetopCodec.CleanProductName("BEITONG T9 For Keyboard & Mouse"));
        Assert.Equal("T9", BetopCodec.CleanProductName("BETOP T9 For PC"));
        Assert.Equal("T9", BetopCodec.CleanProductName("北通 T9 For PC"));
    }

    [Fact]
    public void P2_没有尾巴时也去掉厂商前缀()
    {
        Assert.Equal("KP20EB", BetopCodec.CleanProductName("KP20EB"));
        Assert.Equal("KP20EB", BetopCodec.CleanProductName("BEITONG KP20EB"));
        Assert.Equal("BTP-KP20EB", BetopCodec.CleanProductName("BETOP-BTP-KP20EB"));
    }

    [Fact]
    public void P3_空产品串返回空串()
    {
        Assert.Equal("", BetopCodec.CleanProductName(null));
        Assert.Equal("", BetopCodec.CleanProductName(""));
        Assert.Equal("", BetopCodec.CleanProductName("BEITONG"));   // 只剩厂商名 → 收拾后为空
    }
}
