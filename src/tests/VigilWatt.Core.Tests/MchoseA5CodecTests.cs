using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Mchose;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： A5 V2 Ultra 反码 Feature 协议的实机样本与边界测试。
/// 2026/10/19 20:39:58 kaniu
/// </summary>
public class MchoseA5CodecTests
{
    private static byte[] Sample() => Convert.FromHexString("11F91BBEFEEEF9FDF9FFF69BFF9BFFFFFFFFFFFFFFFF");

    [Fact]
    public void QueryMatchesOfficialInvertedCommand()
    {
        var payload = MchoseA5Codec.BuildBatteryQueryPayload();
        Assert.Equal(20, payload.Length);
        Assert.Equal(0xF9, payload[0]);
        Assert.All(payload[1..], b => Assert.Equal(0xFF, b));
    }

    [Fact]
    public void WaitsForBatteryNotificationInsteadOfOtherCommandOrInput()
    {
        Assert.True(MchoseA5Codec.IsBatteryNotification(Convert.FromHexString("131AF9FFFFFFFF")));
        Assert.False(MchoseA5Codec.IsBatteryNotification(Convert.FromHexString("131AFCFFFFFFFF")));
        Assert.False(MchoseA5Codec.IsBatteryNotification(Convert.FromHexString("111AF9FFFFFFFF")));
        Assert.False(MchoseA5Codec.IsBatteryNotification(Convert.FromHexString("131A")));
    }

    [Fact]
    public void ParsesRealHundredPercentResponseAndPaddedWindowsBuffer()
    {
        var frame = Sample();
        Assert.True(MchoseA5Codec.TryParse(frame, out int percent, out var charging));
        Assert.Equal(100, percent);
        Assert.Equal(ChargingState.Discharging, charging);
        Array.Resize(ref frame, 65);
        Assert.True(MchoseA5Codec.TryParse(frame, out percent, out _));
        Assert.Equal(100, percent);
    }

    [Theory]
    [InlineData(0, 0, ChargingState.Discharging)]
    [InlineData(1, 1, ChargingState.Charging)]
    [InlineData(100, 2, ChargingState.Full)]
    public void DecodesBatteryAndChargeBytes(int level, int charge, ChargingState expected)
    {
        var frame = Sample();
        frame[11] = (byte)(level ^ 0xFF);
        frame[12] = (byte)(charge ^ 0xFF);
        Assert.True(MchoseA5Codec.TryParse(frame, out int percent, out var charging));
        Assert.Equal(level, percent);
        Assert.Equal(expected, charging);
    }

    [Theory]
    [InlineData(0, 0x12)]
    [InlineData(1, 0xFC)]
    [InlineData(2, 0xFF)]
    [InlineData(4, 0xFF)]
    [InlineData(11, 0x9A)]
    [InlineData(12, 0xFC)]
    public void RejectsWrongCommandIdentityAndInvalidValues(int offset, byte value)
    {
        var frame = Sample();
        frame[offset] = value;
        Assert.False(MchoseA5Codec.TryParse(frame, out _, out _));
    }

    [Fact]
    public void RejectsTruncatedAndEmptyResponses()
    {
        Assert.False(MchoseA5Codec.TryParse(Sample()[..12], out _, out _));
        Assert.False(MchoseA5Codec.TryParse([], out _, out _));
    }
}
