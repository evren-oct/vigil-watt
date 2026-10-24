using VigilWatt.Core.Plugins.Atk;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： ATK 鼠标 16 字节 op 帧（校验和保护）的回归测试。
/// 2026/10/20 10:30:00 MOSS
/// </summary>
public sealed class AtkCodecTests
{
    [Fact]
    public void QueryMatchesComputedChecksum()
    {
        // report ID 0x08 + op=0x04 + 14 补零 + 校验和(0x4D - 0x04)
        var expected = Convert.FromHexString("0804000000000000000000000000000049");
        Assert.Equal(expected, AtkCodec.CreateQuery());
    }

    [Fact]
    public void CapturedResponseIs85Percent()
    {
        var response = Convert.FromHexString("08040000000055000000000000000000F4");
        Assert.True(AtkCodec.TryParse(response, out int percent));
        Assert.Equal(85, percent);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(50, true)]
    [InlineData(100, true)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(101, false)]
    [InlineData(255, false)]
    public void PercentBoundaries(int percent, bool accepted)
    {
        var input = BuildResponse((byte)percent);
        Assert.Equal(accepted, AtkCodec.TryParse(input, out _));
    }

    [Fact]
    public void CorruptChecksumIsRejected()
    {
        var input = BuildResponse(70);
        input[^1] ^= 1;
        Assert.False(AtkCodec.TryParse(input, out _));
    }

    [Fact]
    public void WrongOpCodeIsRejected()
    {
        var input = BuildResponse(70);
        input[1] = 0x05; // 不是电量 opcode
        Assert.False(AtkCodec.TryParse(input, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(18)]
    public void WrongLengthIsRejected(int length)
        => Assert.False(AtkCodec.TryParse(new byte[length], out _));

    [Theory]
    [InlineData(0x373B, 0xFF02, 17, 17, 0x08, true)]
    [InlineData(0x373B, 0xFF02, 65, 65, 0x08, false)]
    [InlineData(0x373B, 0xFF03, 17, 17, 0x08, false)]
    [InlineData(0x373B, 0xFF02, 17, 17, 0x07, false)]
    [InlineData(0x373C, 0xFF02, 17, 17, 0x08, false)]
    public void OnlyVerifiedMouseInterfaceIsEligible(ushort vid, ushort page,
        ushort input, ushort output, byte id, bool expected)
        => Assert.Equal(expected, AtkCodec.IsSupportedInterface(vid, page, input, output, id));

    [Fact]
    public void UnknownReportIdIsRejected()
        => Assert.False(AtkCodec.IsSupportedInterface(0x373B, 0xFF02, 17, 17, null));

    private static byte[] BuildResponse(byte percent)
    {
        var frame = new byte[AtkCodec.FrameSize];
        frame[0] = AtkCodec.OpBattery;
        frame[5] = percent;
        frame[AtkCodec.FrameSize - 1] = AtkCodec.Checksum(frame);
        return AtkCodec.BuildReport(AtkCodec.MouseReportId, frame, AtkCodec.MouseReportLength);
    }
}
