using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Rapoo;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： RapooCodec —— 雷柏 2.4G 状态帧（report 0x07，19 字节）的纯解析。
/// 2026/10/08 22:25:16 xliu
/// 用例帧全部取自真机日志，其中 R2 是用户给鼠标断电重连那一下抓到的断链帧。
/// </summary>
public class RapooCodecTests
{
    /// <summary>真机（21:37:12）：鼠标正常使用中，94% / 充电标志 1 / RF=0xC8（bit7=1，RSSI=72）。</summary>
    private static readonly byte[] NormalFrame =
        Convert.FromHexString("071004800C800C015E000301001246010202C8");

    /// <summary>真机（21:38:38）：鼠标断电瞬间，RF=0x7F（bit7=0，RSSI=127）——链路断开标志。</summary>
    private static readonly byte[] LinkDownFrame =
        Convert.FromHexString("071004800C800C015E0003010012460102027F");

    /// <summary>真机（21:39:06）：重连后，充电标志由 1 变 0、电量 94→95、RF=0xD2（bit7=1）。</summary>
    private static readonly byte[] ReconnectFrame =
        Convert.FromHexString("071004800C800C005F000301001246010202D2");

    /// <summary>
    /// 真机（2026-10-25，VT2S Air Max 有线）：link=有线(2)、充电标志=2、电量 100、RF=0x00（bit7=0）。
    /// 有线时没有 RF 链路，[18].bit7 恒 0 属**正常语义**，不得当断链。
    /// </summary>
    private static readonly byte[] WiredFrame =
        Convert.FromHexString("0742021A041A04026400030100134B10000100");

    /// <summary>复制一帧并改掉指定下标，用于构造边界用例。</summary>
    private static byte[] WithByte(byte[] frame, int index, byte value)
    {
        var copy = (byte[])frame.Clone();
        copy[index] = value;
        return copy;
    }

    [Fact]
    public void R1_真机正常帧解析出电量与链路在位()
    {
        var s = RapooCodec.Parse(NormalFrame);
        Assert.Equal(RapooFrameKind.Status, s.Kind);
        Assert.Equal(94, s.Percent);
        Assert.Equal(ChargingState.Discharging, s.Charging);
        Assert.Equal((byte)0x01, s.ChargeFlag);
        Assert.Equal("2.4G", s.Link);
        Assert.False(s.LinkDown);
        Assert.Equal(72, s.RfByte & 0x7F);
    }

    [Fact]
    public void R2_回归_真机断链帧必须被识别为链路断开()
    {
        var s = RapooCodec.Parse(LinkDownFrame);
        Assert.Equal(RapooFrameKind.Status, s.Kind);
        Assert.True(s.LinkDown);                 // ← 这就是"设备自己说断链了"
        Assert.Equal((byte)0x7F, s.RfByte);      // bit7=0 且 RSSI=127（无信号哨兵）
        Assert.Equal(94, s.Percent);             // 断链帧上的电量仍然可读
    }

    [Fact]
    public void R3_真机重连帧撤销断链()
    {
        var s = RapooCodec.Parse(ReconnectFrame);
        Assert.Equal(RapooFrameKind.Status, s.Kind);
        Assert.False(s.LinkDown);
        Assert.Equal(95, s.Percent);
        Assert.Equal((byte)0x00, s.ChargeFlag);  // 实测断电重连时充电标志由 1 翻 0
    }

    [Fact]
    public void R4_非0x07帧不算状态帧()
    {
        var other = WithByte(NormalFrame, 0, 0x04);
        Assert.Equal(RapooFrameKind.NotStatus, RapooCodec.Parse(other).Kind);
    }

    [Fact]
    public void R5_长度不足不算状态帧()
    {
        var shortFrame = new byte[RapooCodec.StatusReportLength - 1];
        shortFrame[0] = RapooCodec.StatusReportId;
        Assert.Equal(RapooFrameKind.NotStatus, RapooCodec.Parse(shortFrame).Kind);
    }

    [Fact]
    public void R6_兼容带reportID前缀的布局()
    {
        var prefixed = new byte[20];
        NormalFrame.CopyTo(prefixed, 1);
        var s = RapooCodec.Parse(prefixed);
        Assert.Equal(RapooFrameKind.Status, s.Kind);
        Assert.Equal(94, s.Percent);
    }

    [Fact]
    public void R7_电量归零判为无效但链路位仍可信()
    {
        var zero = WithByte(LinkDownFrame, 8, 0x00);
        var s = RapooCodec.Parse(zero);
        Assert.Equal(RapooFrameKind.StatusInvalidPercent, s.Kind);
        Assert.True(s.LinkDown);   // 越界帧上的链路位照样可信
    }

    [Fact]
    public void R8_电量越界判为无效()
    {
        var over = WithByte(NormalFrame, 8, 101);
        Assert.Equal(RapooFrameKind.StatusInvalidPercent, RapooCodec.Parse(over).Kind);
    }

    [Theory]
    [InlineData(0x00, true)]    // bit7=0 → 断链
    [InlineData(0x7F, true)]    // 真机观测到的断链哨兵
    [InlineData(0x80, false)]   // bit7=1，RSSI=0
    [InlineData(0xC8, false)]   // bit7=1，RSSI=72
    [InlineData(0xFF, false)]   // bit7=1，RSSI=127
    public void R9_链路位只看bit7(byte rfByte, bool expectedLinkDown)
        => Assert.Equal(expectedLinkDown, RapooCodec.IsLinkDown(rfByte));

    [Fact]
    public void R10_有线帧解析出有线模式与满电()
    {
        var s = RapooCodec.Parse(WiredFrame);
        Assert.Equal(RapooFrameKind.Status, s.Kind);
        Assert.Equal(RapooLinkMode.Wired, s.LinkMode);
        Assert.Equal("有线", s.Link);
        Assert.Equal(100, s.Percent);
        // 满电一律判「充满」：设备满电后放底座上 [7] 会反复横跳（0/1/2），
        // 若让 0x02 优先，充电图标就在 Charging/Full 之间连闪。
        Assert.Equal(ChargingState.Full, s.Charging);
        Assert.Equal((byte)0x02, s.ChargeFlag);
    }

    [Fact]
    public void R11_有线模式下bit7恒0不算断链()
    {
        // 有线时没有 RF 链路，[18]=0x00 是正常语义。
        // 旧实现只看 bit7 → 每 3 秒标一次离线、下一帧又判上线 → 充电图标无限闪烁。
        var s = RapooCodec.Parse(WiredFrame);
        Assert.Equal((byte)0x00, s.RfByte);
        Assert.True(RapooCodec.IsLinkDown(s.RfByte));   // 原始 bit7 确实是 0
        Assert.False(s.LinkDown);                       // 但按连接方式判，不算断链
    }

    [Fact]
    public void R12_2_4G模式下bit7为0仍算断链()
    {
        // 回归：2.4G 的断链判据不能被这次改动削弱。
        var s = RapooCodec.Parse(LinkDownFrame);
        Assert.Equal(RapooLinkMode.Wireless2G, s.LinkMode);
        Assert.True(s.LinkDown);
    }

    [Theory]
    [InlineData(0, "2.4G")]
    [InlineData(2, "有线")]
    [InlineData(1, "link1")]
    [InlineData(3, "link3")]
    public void R13_连接方式由低4位决定(int linkRaw, string expectedLink)
    {
        // 枚举本身是 internal，测试方法签名里不能出现；用 Link 字符串断言等价行为。
        var mode = RapooCodec.LinkModeOf(linkRaw);
        var frame = WithByte(NormalFrame, 1, (byte)(linkRaw | 0x10));
        Assert.Equal(expectedLink, RapooCodec.Parse(frame).Link);
        Assert.Equal(mode, RapooCodec.Parse(frame).LinkMode);
    }

    [Fact]
    public void R14_有线帧电量越界时也不当断链()
    {
        var zero = WithByte(WiredFrame, 8, 0x00);
        var s = RapooCodec.Parse(zero);
        Assert.Equal(RapooFrameKind.StatusInvalidPercent, s.Kind);
        Assert.False(s.LinkDown);
    }

    [Fact]
    public void R15_键盘集合挂在鼠标设备上时不能显示成键盘()
    {
        // 真机：VT2S Air Max 有线，认领的 FF00:0002 接口产品串是 "Rapoo keyboard"，
        // 但同 VID:PID 下有 "Rapoo Mouse" 兄弟接口 → 这是一只鼠标。
        var name = RapooCodec.DisplayName("Rapoo keyboard", siblingIsMouse: true);
        Assert.Equal("雷柏 Rapoo Mouse", name);
        Assert.Equal(DeviceKind.Mouse, DeviceKindGuesser.Guess(name));
    }

    [Fact]
    public void R16_真键盘仍显示键盘()
    {
        // 没有 Mouse 兄弟接口 → 保持原样，UI 按名字猜成键盘。
        var name = RapooCodec.DisplayName("Rapoo keyboard", siblingIsMouse: false);
        Assert.Equal("雷柏 Rapoo keyboard", name);
        Assert.Equal(DeviceKind.Keyboard, DeviceKindGuesser.Guess(name));
    }

    [Fact]
    public void R17_产品串本身是Mouse时直接用()
    {
        var name = RapooCodec.DisplayName("Rapoo Mouse", siblingIsMouse: true);
        Assert.Equal("雷柏 Rapoo Mouse", name);
        Assert.Equal(DeviceKind.Mouse, DeviceKindGuesser.Guess(name));
    }

    [Fact]
    public void R18_无产品串时退回品牌加VIDPID()
    {
        Assert.Equal("雷柏 (VID_24AE&PID_4B13)",
            RapooCodec.DisplayName("", siblingIsMouse: true, vidPid: "VID_24AE&PID_4B13"));
    }

    [Fact]
    public void R19_满电时一律判充满不因充电标志抖()
    {
        // 满电后放在充电底座上，设备 [7] 在 0/1/2 之间反复跳（底座涓流检测）。
        // 若让 0x02 优先，充电状态会在 Charging/Full 之间横跳 → 图标连闪。
        foreach (var flag in new byte[] { 0x00, 0x01, 0x02 })
        {
            var frame = WithByte(WiredFrame, 7, flag);
            var s = RapooCodec.Parse(frame);
            Assert.Equal(100, s.Percent);
            Assert.Equal(ChargingState.Full, s.Charging);
        }
    }

    [Fact]
    public void R20_未满电时按充电标志判()
    {
        var charging = RapooCodec.Parse(WithByte(NormalFrame, 7, 0x02));
        Assert.Equal(ChargingState.Charging, charging.Charging);

        var discharging = RapooCodec.Parse(WithByte(NormalFrame, 7, 0x01));
        Assert.Equal(ChargingState.Discharging, discharging.Charging);
    }

    [Fact]
    public void R21_无线模式设备级产品串也按鼠标命名()
    {
        // 无线（1B13）所有接口产品串都是 "Rapoo Gaming Device"，不含 mouse 关键字。
        // 不改的话 UI 猜不出类型，图标退回通用圆点（与有线模式不一致）。
        var name = RapooCodec.DisplayName("Rapoo Gaming Device", siblingIsMouse: true);
        Assert.Equal("雷柏 Rapoo Mouse", name);
        Assert.Equal(DeviceKind.Mouse, DeviceKindGuesser.Guess(name));
    }

    [Fact]
    public void R22_设备级产品串在非鼠标设备上保持原样()
    {
        // 没有鼠标集合 → 别硬加 Mouse
        var name = RapooCodec.DisplayName("Rapoo Gaming Device", siblingIsMouse: false);
        Assert.Equal("雷柏 Rapoo Gaming Device", name);
    }

    [Fact]
    public void R23_孤立的充电标志不判充电()
    {
        // 切换瞬间的过渡帧：flag=2 孤立出现（前后的 flag 都不是 2）
        var d = RapooCodec.ResolveCharging(0x02, lastFlag: 0x01, wasStable: false, percent: 80);
        Assert.Equal(ChargingState.Discharging, d.Charging);
        Assert.False(d.Stable);
    }

    [Fact]
    public void R24_连续两帧充电标志才判充电()
    {
        // 第一帧：上一帧不是 2 → 不判
        var d1 = RapooCodec.ResolveCharging(0x02, lastFlag: 0x01, wasStable: false, percent: 80);
        Assert.Equal(ChargingState.Discharging, d1.Charging);
        // 第二帧：上一帧是 2 → 判充电
        var d2 = RapooCodec.ResolveCharging(0x02, lastFlag: d1.LastFlag, wasStable: d1.Stable, percent: 80);
        Assert.Equal(ChargingState.Charging, d2.Charging);
        Assert.True(d2.Stable);
    }

    [Fact]
    public void R25_充电标志来回跳时状态稳定不闪()
    {
        // 实测放到底座时的过渡序列 2→1→0→2：全程不应出现 Charging
        byte last = 0xFF;
        bool stable = false;
        var seen = new List<ChargingState>();
        foreach (var flag in new byte[] { 0x02, 0x01, 0x00, 0x02 })
        {
            var d = RapooCodec.ResolveCharging(flag, last, stable, percent: 80);
            last = d.LastFlag;
            stable = d.Stable;
            seen.Add(d.Charging);
        }
        Assert.DoesNotContain(ChargingState.Charging, seen);
    }

    [Fact]
    public void R26_满电时去抖直接判充满()
    {
        var d = RapooCodec.ResolveCharging(0x02, lastFlag: 0x02, wasStable: true, percent: 100);
        Assert.Equal(ChargingState.Full, d.Charging);
    }
}
