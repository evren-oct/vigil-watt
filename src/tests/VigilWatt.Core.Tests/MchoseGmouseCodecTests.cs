using VigilWatt.Core.Plugins.Mchose;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 迈从 Gmouse 主动查询协议（G3 V2 3837:4245 / 蝰蛇 X11 A8A5:2255）编解码的回归测试。
/// 2026/10/20 Sunshine
/// 帧来自官方 M HUB /gmouse 与社区 aliezzzz/mchose-battery；真机读到 89% 未充电，见 mchose.md §4。
/// </summary>
public sealed class MchoseGmouseCodecTests
{
    /// <summary>本机蝰蛇 X11（A8A5:2255）的接收器产品串；产品串不含它就不认新 VID。</summary>
    private const string KsnakeProduct = "USB Receiver";
    /// <summary>真机 Gmouse 电量应答：前缀 AA 30，电量在 [8] = 0x59 = 89%，充电在 [9] = 0。</summary>
    private const string BatteryReply =
        "AA30" + "000000000000" + "59" + "00";

    private static byte[] Frame(string hex, int totalLength = MchoseGmouseCodec.FrameLength)
    {
        var bytes = Convert.FromHexString(hex);
        var frame = new byte[Math.Max(totalLength, bytes.Length)];
        bytes.CopyTo(frame, 0);
        return frame;
    }

    /// <summary>查询帧：<c>00 55 30 A5 0B 2E 01 01 01</c>（余下补零），总长 65。</summary>
    [Fact]
    public void CreateQueryMatchesOfficialFrame()
    {
        var frame = MchoseGmouseCodec.CreateQuery();

        Assert.Equal(MchoseGmouseCodec.FrameLength, frame.Length);
        Assert.Equal("005530A50B2E010101", Convert.ToHexString(frame[..9]));
        Assert.All(frame[9..], b => Assert.Equal(0, b));
    }

    /// <summary>真机应答：电量在 [offset+8]、充电在 [offset+9]。</summary>
    [Fact]
    public void ParsesBatteryReply()
    {
        Assert.True(MchoseGmouseCodec.TryParse(Frame(BatteryReply), out int percent, out bool charging));

        Assert.Equal(89, percent);
        Assert.False(charging);
    }

    /// <summary>充电标志非 0 时应判为充电中。</summary>
    [Fact]
    public void ParsesChargingReply()
    {
        var charging = Frame("AA30" + "000000000000" + "64" + "01");

        Assert.True(MchoseGmouseCodec.TryParse(charging, out int percent, out bool isCharging));

        Assert.Equal(100, percent);
        Assert.True(isCharging);
    }

    /// <summary>ReadInput 缓冲带 report ID 前缀（偏移 1）时同样要能解析。</summary>
    [Fact]
    public void ParsesReplyWithReportIdPrefix()
    {
        var prefixed = Frame("00" + BatteryReply, MchoseGmouseCodec.FrameLength + 1);

        Assert.True(MchoseGmouseCodec.TryParse(prefixed, out int percent, out _));
        Assert.Equal(89, percent);
    }

    /// <summary>
    /// 本机蝰蛇 X11 实录：放电 80%（<c>[8]=0x50</c>）与插线充电 81%（<c>[8]=0x51 / [9]=0x01</c>）。
    /// 同一只鼠标只差第二字节，故 <c>[9]</c> 是非零即充电中这条判据首次有真机对照（原为未验证，见 mchose.md §4.5）。
    /// </summary>
    [Theory]
    [InlineData("AA30A50B0A0101015000", 80, false)]   // 未充电
    [InlineData("AA30A50B0A0101015101", 81, true)]    // 插线充电中
    public void ParsesKsnakeX11RealReplies(string hex, int expectedPercent, bool expectedCharging)
    {
        Assert.True(MchoseGmouseCodec.TryParse(Frame(hex), out int percent, out bool charging));

        Assert.Equal(expectedPercent, percent);
        Assert.Equal(expectedCharging, charging);
    }

    /// <summary>前缀不是 AA 30 的帧（其它协议的帧）不算应答。</summary>
    [Theory]
    [InlineData("4D01011000090000")]  // K7 的 0x4D 帧
    [InlineData("FAFB070000")]        // QHW 的 CZ 推送
    [InlineData("0000000000000000")]  // 全零噪声
    public void ForeignFramesAreNotParsed(string hex)
        => Assert.False(MchoseGmouseCodec.TryParse(Frame(hex), out _, out _));

    /// <summary>电量越界（0 是"还没算出来"的占位、>100 不可信）不能报出去。</summary>
    [Theory]
    [InlineData("AA30" + "000000000000" + "00" + "00")]  // 电量 0
    [InlineData("AA30" + "000000000000" + "65" + "00")]  // 电量 101
    public void RejectsImplausiblePercent(string hex)
        => Assert.False(MchoseGmouseCodec.TryParse(Frame(hex), out _, out _));

    /// <summary>接口指纹：只有完整命中才算 Gmouse，同 VID 的其它 PID / 用法一律不认。</summary>
    [Theory]
    [InlineData(0x3837, 0x4245, 0xFF01, 0x0010, 65, 65, true)]   // 真机命令通道
    [InlineData(0x3837, 0x4245, 0xFF01, 0x0010, 66, 66, true)]   // 长度富余也认
    [InlineData(0x3837, 0x1018, 0xFF01, 0x0001, 64, 64, false)]  // K7 命令通道（usage 不同）
    [InlineData(0x3837, 0x100A, 0xFF00, 0x0002, 65, 65, false)]  // A7 V2 Pro 接收器
    [InlineData(0x3837, 0x201B, 0xFF02, 0x0002, 20, 20, false)]  // 键盘 V1 会话通道
    [InlineData(0x3837, 0x3033, 0xFF00, 0x0002, 64, 64, false)]  // QHW 世代 dongle
    [InlineData(0x41E4, 0x4245, 0xFF01, 0x0010, 65, 65, false)]  // 老款 VID
    public void OnlyExactFingerprintIsGmouse(ushort vid, ushort pid, ushort page, ushort usage,
        ushort input, ushort output, bool expected)
        => Assert.Equal(expected, MchoseGmouseCodec.IsSupportedInterface(vid, pid, "USB Receiver", page, usage, input, output));

    /// <summary>
    /// 本机蝰蛇 X11（A8A5:2255）：命令通道 0xFF01/0x0010 双向 65 字节才算；同一接收器的 0xFF06/0x0002（64/64）
    /// 与 0xFF05/0x0002（64/0）都不是通道。产品串不含 Receiver 的兄弟型号也一律不认。
    /// </summary>
    [Theory]
    [InlineData(KsnakeProduct, 0xFF01, 0x0010, 65, 65, true)]    // 真机 mi_02 命令通道
    [InlineData(KsnakeProduct, 0xFF06, 0x0002, 64, 64, false)]   // 同 dongle 的兄弟集合，无输出通道
    [InlineData(KsnakeProduct, 0xFF05, 0x0002, 64, 0, false)]    // 同 dongle 的兄弟集合，无输出通道
    [InlineData(KsnakeProduct, 0xFFA5, 0x0088, 2, 0, false)]     // 同 dongle 的 2 字节集合
    [InlineData(KsnakeProduct, 0xFF01, 0x0010, 64, 64, false)]   // 缺 report ID 那 1 字节
    [InlineData("K-snake X11", 0xFF01, 0x0010, 65, 65, false)]   // 产品串不是接收器
    [InlineData("", 0xFF01, 0x0010, 65, 65, false)]              // 读不到产品串就不认
    public void KsnakeReceiverIsRecognizedByProductMarker(string product, ushort page, ushort usage,
        ushort input, ushort output, bool expected)
        => Assert.Equal(expected, MchoseGmouseCodec.IsSupportedInterface(
            MchoseGmouseCodec.VidKsnake, MchoseGmouseCodec.PidKsnake, product, page, usage, input, output));

    /// <summary>兄弟接口闸门只放行新 VID 的新 PID：迈从自己的 G3 V2 不受影响（它的兄弟集合仍按原判据走）。</summary>
    [Theory]
    [InlineData(0xA8A5, 0x2255, true)]
    [InlineData(0x3837, 0x4245, false)]
    public void SiblingGateOnlyCoversKsnakeReceiver(ushort vid, ushort pid, bool expected)
        => Assert.Equal(expected, MchoseGmouseCodec.IsSiblingVendorInterface(vid, pid));
}
