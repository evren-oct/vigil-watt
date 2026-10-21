using VigilWatt.Core.Plugins.Mchose;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 迈从 Gmouse 主动查询协议（G3 V2，3837:4245）编解码的回归测试。
/// 2026/10/20 Sunshine
/// 查询帧与应答前缀来自官方 M HUB 网页端 /gmouse 与社区 aliezzzz/mchose-battery；
/// 真机 G3 V2（3837:4245）验证读到 89% 未充电。帧结构见 docs/hardware/mchose.md §4。
/// </summary>
public sealed class MchoseGmouseCodecTests
{
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
        => Assert.Equal(expected, MchoseGmouseCodec.IsSupportedInterface(vid, pid, page, usage, input, output));
}
