using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Cherry;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： CHERRY 查询帧、实机响应和错误帧的回归测试。
/// 2026/10/19 20:39:57 kaniu
/// </summary>
public class CherryCodecTests
{
    [Fact]
    public void QueryMatchesOfficialCommand()
    {
        var frame = CherryCodec.BuildBatteryQuery();
        Assert.Equal(64, frame.Length);
        Assert.Equal(Convert.FromHexString("0420001A06000000"), frame[..8]);
        Assert.All(frame[8..], b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(92)]
    [InlineData(100)]
    public void ParsesBatteryWithoutTreatingLowValuesAsSleep(byte level)
    {
        var frame = CherryCodec.BuildBatteryQuery();
        frame[8] = level;
        Assert.True(CherryCodec.TryParse(frame, out int percent, out var charging));
        Assert.Equal(level, percent);
        Assert.Equal(ChargingState.Discharging, charging);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 0x21)]
    [InlineData(2, 1)]
    [InlineData(3, 0xAA)]
    [InlineData(4, 5)]
    [InlineData(5, 1)]
    [InlineData(6, 1)]
    [InlineData(7, 0xFF)]
    [InlineData(8, 101)]
    [InlineData(8, 255)]
    public void RejectsOtherCommandsAndErrorResponses(int offset, byte value)
    {
        var frame = CherryCodec.BuildBatteryQuery();
        frame[8] = 92;
        frame[offset] = value;
        Assert.False(CherryCodec.TryParse(frame, out _, out _));
    }

    [Fact]
    public void RejectsTruncationAndKeepsUncalibratedPowerStateUnknown()
    {
        var frame = CherryCodec.BuildBatteryQuery();
        frame[9] = 1;
        Assert.True(CherryCodec.TryParse(frame, out _, out var charging));
        Assert.Equal(ChargingState.Unknown, charging);
        Assert.False(CherryCodec.TryParse(frame[..14], out _, out _));
        Assert.False(CherryCodec.TryParse([], out _, out _));
    }
}
