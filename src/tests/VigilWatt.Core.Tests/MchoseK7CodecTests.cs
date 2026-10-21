using VigilWatt.Core.Plugins.Mchose;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 迈从 K7 V2 Ultra+（3837:1018）编解码的回归测试。
/// 2026/10/21 Mightling
/// 全部向量都是**真机抓到的原始帧**（2026-10-21 本机 K7 V2 Ultra+）：设备信息应答、睡着了只回的 0xFF、
/// 以及两条 0x0600 状态事件。帧结构见 docs/hardware/protocols.md §13。
/// </summary>
public sealed class MchoseK7CodecTests
{
    /// <summary>真机 0x0900 设备信息应答：连接状态 2、充电状态 0、电量 0x64 = 100%。</summary>
    private const string DeviceInfoReply =
        "4D01011000090000" + "373826400400000000100200640008E4" + "EF";

    /// <summary>真机常态应答：鼠标在睡，dongle 只回 1 字节 0xFF（payload 长度 1）。</summary>
    private const string SleepingReply = "4D010101000900" + "00FF";

    /// <summary>真机 0x0600 电量事件：payload[0] bit1 置位，电量在 payload[3] = 0x64。</summary>
    private const string BatteryEvent =
        "4D01010D00060000" + "020000640000000000000000" + "00" + "6C";

    /// <summary>真机 0x0600 充电事件（在底座上）：payload[1] bit1 置位，充电状态在 payload[12] = 1。</summary>
    private const string ChargeEventOnDock =
        "4D01010D00060000" + "000200000000000000000000" + "01" + "09";

    /// <summary>真机 0x0600 充电事件（已离开底座）：payload[12] = 0。</summary>
    private const string ChargeEventOffDock =
        "4D01010D00060000" + "000200000000000000000000" + "00" + "08";

    private static byte[] Frame(string hex, int totalLength = MchoseK7Codec.FrameLength)
    {
        var bytes = Convert.FromHexString(hex);
        var frame = new byte[Math.Max(totalLength, bytes.Length)];
        bytes.CopyTo(frame, 0);
        return frame;
    }

    /// <summary>读设备信息的请求帧：<c>4D 01 01 00 00 09 00 00 08</c>（余下补零）。</summary>
    [Fact]
    public void BuildDeviceInfoQueryMatchesOfficialFrame()
    {
        var frame = MchoseK7Codec.BuildDeviceInfoQuery();

        Assert.Equal(MchoseK7Codec.FrameLength, frame.Length);
        Assert.Equal("4D0101000009000008", Convert.ToHexString(frame[..9]));
        // 校验之后全是补零，不能带出脏字节
        Assert.All(frame[9..], b => Assert.Equal(0, b));
    }

    /// <summary>校验是"XOR frame[2..7+len]"：payload 非空时校验位跟着变。</summary>
    [Fact]
    public void BuildFrameXorsOnlyFlagsToPayload()
    {
        var frame = MchoseK7Codec.BuildFrame(MchoseK7Codec.CmdDeviceInfo, 0x11, 0x22);

        // 01 ^ 01 ^ 02 ^ 00 ^ 09 ^ 00 ^ 00 ^ 11 ^ 22 = 0x39
        Assert.Equal(0x02, frame[3]);
        Assert.Equal(0x11, frame[8]);
        Assert.Equal(0x22, frame[9]);
        Assert.Equal(0x39, frame[10]);
        Assert.Equal(MchoseK7Codec.ReportId, frame[0]);
    }

    /// <summary>真机应答：payload[10]=连接状态、[11]=充电状态、[12]=电量。</summary>
    [Fact]
    public void ParsesRealDeviceInfoReply()
    {
        Assert.True(MchoseK7Codec.TryParseDeviceInfo(Frame(DeviceInfoReply), out int percent, out byte charge, out byte connect));

        Assert.Equal(100, percent);
        Assert.Equal(0, charge);
        Assert.Equal(2, connect);
    }

    /// <summary>ReadInput 缓冲带 report ID 前缀（偏移 1）时同样要能解析。</summary>
    [Fact]
    public void ParsesDeviceInfoReplyWithReportIdPrefix()
    {
        var prefixed = Frame("00" + DeviceInfoReply, MchoseK7Codec.FrameLength + 1);

        Assert.True(MchoseK7Codec.TryParseDeviceInfo(prefixed, out int percent, out _, out _));
        Assert.Equal(100, percent);
    }

    /// <summary>"鼠标在睡"的 1 字节 0xFF 应答：不是读数，但**算有应答**——这条区分是账本的关键。</summary>
    [Fact]
    public void SleepingReplyIsAnAnswerButNotAReading()
    {
        var sleeping = Frame(SleepingReply);

        Assert.True(MchoseK7Codec.LooksLikeReply(sleeping));
        Assert.False(MchoseK7Codec.TryParseDeviceInfo(sleeping, out _, out _, out _));
    }

    /// <summary>别的设备/别家的帧（首字节不是 0x4D）不算应答，否则会永远不判离线。</summary>
    [Theory]
    [InlineData("FAFB0700")]          // QHW 的 CZ 推送
    [InlineData("4D02010100090000FF")] // version 不是 1
    [InlineData("0000")]               // 全零噪声
    public void ForeignFramesAreNotReplies(string hex)
        => Assert.False(MchoseK7Codec.LooksLikeReply(Frame(hex)));

    /// <summary>电量越界（0 是"还没算出来"的占位）不能当 0% 报出去。</summary>
    [Fact]
    public void RejectsImplausiblePercentInDeviceInfo()
    {
        // 把 payload[12] 从 0x64 改成 0x00
        var broken = Frame(DeviceInfoReply.Replace("020064", "020000"));

        Assert.False(MchoseK7Codec.TryParseDeviceInfo(broken, out _, out _, out _));
    }

    /// <summary>电量事件：payload[0] bit1 置位 → 电量在 payload[3]，本条不带充电状态。</summary>
    [Fact]
    public void ParsesRealBatteryEvent()
    {
        Assert.True(MchoseK7Codec.TryParseStatusEvent(Frame(BatteryEvent), out var evt));

        Assert.Equal(100, evt.Percent);
        Assert.Null(evt.ChargeStatus);
    }

    /// <summary>充电事件：payload[1] bit1 置位 → 充电状态在 payload[12]，本条不带电量。</summary>
    [Theory]
    [InlineData(ChargeEventOnDock, 1)]
    [InlineData(ChargeEventOffDock, 0)]
    public void ParsesRealChargeEvent(string hex, int expected)
    {
        Assert.True(MchoseK7Codec.TryParseStatusEvent(Frame(hex), out var evt));

        Assert.Null(evt.Percent);
        Assert.Equal((byte?)expected, evt.ChargeStatus);
    }

    /// <summary>两个位图都没置位（DPI / 配置索引之类的子类型）就不是电源事件。</summary>
    [Fact]
    public void IgnoresStatusEventWithoutPowerBits()
        => Assert.False(MchoseK7Codec.TryParseStatusEvent(
            Frame("4D01010D0006000000000000000000000000000000" + "00"), out _));

    /// <summary>接口指纹：只有完整命中才算 K7，同 VID 的其它 PID / 用法一律不认。</summary>
    [Theory]
    [InlineData(0x3837, 0x1018, 0xFF01, 0x0001, 64, 64, true)]   // 真机命令通道
    [InlineData(0x3837, 0x1018, 0xFF01, 0x0001, 65, 65, true)]   // 长度富余也认（固件可能多 1 字节）
    [InlineData(0x3837, 0x3033, 0xFF00, 0x0002, 64, 64, false)]  // QHW 世代 dongle
    [InlineData(0x3837, 0x100A, 0xFF00, 0x0002, 65, 65, false)]  // A7 V2 Pro 接收器
    [InlineData(0x3837, 0x1018, 0xFF0B, 0x00C4, 61, 61, false)]  // 同 dongle 的另一条静默接口
    [InlineData(0x3837, 0x201B, 0xFF02, 0x0002, 20, 20, false)]  // 键盘 V1 会话通道
    [InlineData(0x291D, 0x385D, 0xFF01, 0x0001, 64, 64, false)]  // V9 Pro 耳机的 VID
    public void OnlyExactFingerprintIsK7(ushort vid, ushort pid, ushort page, ushort usage,
        ushort input, ushort output, bool expected)
        => Assert.Equal(expected, MchoseK7Codec.IsSupportedInterface(vid, pid, page, usage, input, output));
}
