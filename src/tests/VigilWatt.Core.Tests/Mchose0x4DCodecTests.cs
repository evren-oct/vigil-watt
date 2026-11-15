using VigilWatt.Core.Plugins.Mchose;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 迈从 0x4D 设备信息帧的编解码用例。样本按官方 `getCommandBuffer` / `deviceInfoToObj` 合成，
/// 真机字段见 docs/hardware/mchose.md §2.4（2026-11-16 本机 92% 验证）。
/// </summary>
public class Mchose0x4DCodecTests
{
    [Fact]
    public void BuildDeviceInfoQuery_MatchesOfficialLayout()
    {
        var frame = Mchose0x4DCodec.BuildDeviceInfoQuery();

        Assert.Equal(64, frame.Length);
        Assert.Equal(new byte[] { 0x4D, 0x01, 0x01, 0x00, 0x00, 0x09, 0x00, 0x00, 0x08 }, frame[..9]);
        // 校验 = 从 [2] 起到最后一个 payload 字节的异或（此处 [2..7]），官方只在 flags==1 时写
        Assert.Equal((byte)(0x01 ^ 0x00 ^ 0x00 ^ 0x09 ^ 0x00 ^ 0x00), frame[8]);
        Assert.All(frame[9..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void TryParseDeviceInfo_ReadsPercentChargeAndConnect()
    {
        var reply = Reply(percent: 57, charge: 1, connect: 2);

        Assert.True(Mchose0x4DCodec.TryParseDeviceInfo(reply, out var percent, out var charge, out var connect));
        Assert.Equal(57, percent);
        Assert.Equal(1, charge);
        Assert.Equal(2, connect);
    }

    [Fact]
    public void TryParseDeviceInfo_AcceptsBufferWithoutReportIdPrefix()
    {
        var reply = Reply(percent: 88, charge: 0, connect: 2);
        var shifted = reply[1..];

        Assert.True(Mchose0x4DCodec.TryParseDeviceInfo(shifted, out var percent, out _, out _));
        Assert.Equal(88, percent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(255)]
    public void TryParseDeviceInfo_RejectsImplausiblePercent(byte value)
    {
        var reply = Reply(percent: value, charge: 0, connect: 2);

        Assert.False(Mchose0x4DCodec.TryParseDeviceInfo(reply, out _, out _, out _));
    }

    [Fact]
    public void TryParseDeviceInfo_RejectsShortPayload()
    {
        var reply = Reply(percent: 50, charge: 0, connect: 2);
        reply[3] = 5;   // 声明长度装不下 payload[12]

        Assert.False(Mchose0x4DCodec.TryParseDeviceInfo(reply, out _, out _, out _));
    }

    [Fact]
    public void TryParseDeviceInfo_RejectsOtherCommand()
    {
        var reply = Reply(percent: 50, charge: 0, connect: 2);
        reply[4] = 0x04;   // 0x0904 是旧版误读的 getBattery，不是设备信息

        Assert.False(Mchose0x4DCodec.TryParseDeviceInfo(reply, out _, out _, out _));
    }

    private static byte[] Reply(byte percent, byte charge, byte connect)
    {
        var frame = new byte[64];
        frame[0] = 0x4D;
        frame[1] = 0x01;
        frame[2] = 0x01;
        frame[3] = 14;
        frame[4] = 0x00;
        frame[5] = 0x09;
        frame[6] = 0x00;
        frame[7] = 0x00;
        frame[8 + 10] = connect;
        frame[8 + 11] = charge;
        frame[8 + 12] = percent;
        return frame;
    }
}
