using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Vgn;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： VgnY2Codec —— 蜻蜓 F2 大师+（vgn-y2）的帧构造、应答解析与接口匹配。
/// 2026/10/21 新增
///
/// 用例用的是**真机原始帧**（3554:FB56，<c>Dragonfly F2 Master+</c>，2026-10-21 抓于本机，未插线）：
/// <code>
///   发 08 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 49
///   收 08 04 00 00 00 02 50 00 0F C8 00 00 00 00 00 00 20
/// </code>
/// 电量在 payload[5]=0x50=80%，payload[6]=0 未充电，payload[7..8]=0x0FC8=4040mV。
/// </summary>
public sealed class VgnY2CodecTests
{
    /// <summary>真机 BatteryLevel 应答（含 report ID 首字节，共 17 字节）。</summary>
    private static byte[] RealBatteryFrame()
        => Convert.FromHexString("08040000000250000FC800000000000020");

    /// <summary>同一次会话里 DeviceOnLine 的应答：电量位不在同一下标，必须不能被当成电量帧。</summary>
    private static byte[] RealOnlineFrame()
        => Convert.FromHexString("0803000000010135D9BF0000000000007B");

    [Fact]
    public void QueryFrameMatchesOfficialVhub()
    {
        // VHUB 的 JmServe.sendCommand(4)：16 字节全零 + [0]=4，校验 (85-4-8)&0xFF = 0x49
        Assert.Equal("0804000000000000000000000000000049",
            Convert.ToHexString(VgnY2Codec.BuildCommand(VgnY2Codec.CmdBatteryLevel)));
        Assert.Equal("080300000000000000000000000000004A",
            Convert.ToHexString(VgnY2Codec.BuildCommand(VgnY2Codec.CmdDeviceOnline)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CapturedBatteryFrameIs80Percent(bool withReportId)
    {
        var frame = RealBatteryFrame();
        var input = withReportId ? frame : frame[1..];

        Assert.True(VgnY2Codec.TryParseBattery(input, out int percent, out var charging, out int voltageMv));
        Assert.Equal(80, percent);
        Assert.Equal(ChargingState.Discharging, charging);
        Assert.Equal(4040, voltageMv);
    }

    [Fact]
    public void OnlineFrameIsNotMistakenForBattery()
    {
        var frame = RealOnlineFrame();
        Assert.False(VgnY2Codec.TryParseBattery(frame, out _, out _, out _));
        Assert.True(VgnY2Codec.TryParseOnline(frame, out bool online));
        Assert.True(online);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(16)]
    public void CorruptFramesAreRejected(int offset)
    {
        var frame = RealBatteryFrame();
        frame[offset] ^= 0x01;
        Assert.False(VgnY2Codec.TryParseBattery(frame, out _, out _, out _));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    [InlineData(255, false)]
    public void PercentMustBeNonzeroAndAtMost100(int percent, bool valid)
    {
        var payload = new byte[VgnY2Codec.PayloadLength];
        payload[0] = VgnY2Codec.CmdBatteryLevel;
        payload[5] = (byte)percent;
        payload[15] = VgnY2Codec.Checksum(payload);

        var frame = new byte[VgnY2Codec.FrameLength];
        frame[0] = VgnY2Codec.ReportId;
        payload.CopyTo(frame, 1);
        Assert.Equal(valid, VgnY2Codec.TryParseBattery(frame, out _, out _, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(32)]
    public void WrongLengthsAreRejected(int length)
        => Assert.False(VgnY2Codec.TryParseBattery(new byte[length], out _, out _, out _));

    [Fact]
    public void ChargingFlagAndFullCharge()
    {
        var payload = new byte[VgnY2Codec.PayloadLength];
        payload[0] = VgnY2Codec.CmdBatteryLevel;
        payload[5] = 100;
        payload[6] = 1;                       // 充电中
        payload[7] = 0x10;
        payload[8] = 0x5E;                    // 4190mV
        payload[15] = VgnY2Codec.Checksum(payload);

        var frame = new byte[VgnY2Codec.FrameLength];
        frame[0] = VgnY2Codec.ReportId;
        payload.CopyTo(frame, 1);

        Assert.True(VgnY2Codec.TryParseBattery(frame, out int percent, out var charging, out int mv));
        Assert.Equal(100, percent);
        Assert.Equal(ChargingState.Full, charging);
        Assert.Equal(4190, mv);

        payload[5] = 60;
        payload[15] = VgnY2Codec.Checksum(payload);
        payload.CopyTo(frame, 1);
        Assert.True(VgnY2Codec.TryParseBattery(frame, out _, out charging, out _));
        Assert.Equal(ChargingState.Charging, charging);
    }

    [Theory]
    [InlineData(0x3554, 0xFB56, 0xFF02, 0x0002, 17, 17, true)]   // 本机接收器
    [InlineData(0x3554, 0xFB57, 0xFF02, 0x0002, 17, 17, true)]   // 同族有线（未复测，同协议）
    [InlineData(0x3554, 0xFA09, 0xFF02, 0x0002, 17, 17, false)]  // 狼蛛 F99 Pro 的 PID，不归 VGN
    [InlineData(0x3554, 0xFB56, 0xFF02, 0x0002, 20, 20, false)]  // 20/20 是 0x13 会话协议，不是 y2
    [InlineData(0x3554, 0xFB56, 0xFF04, 0x0002, 17, 17, false)]  // 同族 feature 接口不参与
    [InlineData(0x391D, 0xFB56, 0xFF02, 0x0002, 17, 17, false)]  // F1 的 VID，不认
    public void OnlyCapturedInterfaceIsEligible(ushort vid, ushort pid, ushort page, ushort usage,
        int input, int output, bool eligible)
        => Assert.Equal(eligible, VgnY2Codec.IsMatch(vid, pid, page, usage, input, output));
}
