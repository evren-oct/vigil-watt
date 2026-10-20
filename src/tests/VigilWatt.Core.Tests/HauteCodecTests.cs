using System.Text;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Haute;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： Haute 固件识别、电量、充电和无线断开状态的回归测试。
/// 2026/10/19 20:39:59 kaniu
/// </summary>
public class HauteCodecTests
{
    [Fact]
    public void ChipIdGivesStableIdentityIndependentOfUsbPath()
    {
        var frame = Convert.FromHexString("FA010203040506000001");
        Assert.True(HauteCodec.TryParseDeviceId(frame, out var first));
        Assert.Equal("haute:chip-0102030405060000", first);
        frame[9] = 0;
        Assert.True(HauteCodec.TryParseDeviceId(frame, out var second));
        Assert.Equal(first, second);
        frame[1]++;
        Assert.True(HauteCodec.TryParseDeviceId(frame, out var other));
        Assert.NotEqual(first, other);
    }

    [Theory]
    [InlineData("")]
    [InlineData("FA01020304050607")]
    [InlineData("F90102030405060708")]
    [InlineData("FA0000000000000000")]
    [InlineData("FAFFFFFFFFFFFFFFFF")]
    public void InvalidIdentityCannotCollapseAllReceiversIntoOne(string hex)
        => Assert.False(HauteCodec.TryParseDeviceId(Convert.FromHexString(hex), out _));

    [Theory]
    [InlineData("haute:A20A287A3BDF8843", true)]
    [InlineData("HAUTE:04a18f2481638b2f", true)]
    [InlineData("haute:chip-0102030405060000", false)]
    [InlineData("cherry:A20A287A3BDF8843", false)]
    [InlineData("haute:GGGGGGGGGGGGGGGG", false)]
    public void OnlyUnreliableLegacyPathIdsAreExcludedFromTombstoneRestoration(string id, bool legacy)
        => Assert.Equal(legacy, HauteCodec.IsLegacyPathId(id));

    private static byte[] Sample() => Convert.FromHexString("F9FB800000000000000000004600");

    [Fact]
    public void RequiresHauteFirmwareInsteadOfClaimingEverySonyController()
    {
        Assert.True(HauteCodec.IsHauteFirmware([0xF8, .. Encoding.ASCII.GetBytes("Haute42 3.23")]));
        Assert.False(HauteCodec.IsHauteFirmware([0xF8, .. Encoding.ASCII.GetBytes("Sony 3.23")]));
        Assert.False(HauteCodec.IsHauteFirmware([0xF9, .. Encoding.ASCII.GetBytes("Haute42 3.23")]));
        Assert.False(HauteCodec.IsHauteFirmware([]));
    }

    [Fact]
    public void ParsesRealSeventyPercentResponse()
    {
        Assert.True(HauteCodec.TryParse(Sample(), out var percent, out var charging));
        Assert.Equal(70, percent);
        Assert.Equal(ChargingState.Discharging, charging);
    }

    [Theory]
    [InlineData(0, 0, ChargingState.Discharging)]
    [InlineData(1, 1, ChargingState.Charging)]
    [InlineData(100, 2, ChargingState.Full)]
    [InlineData(50, 0x81, ChargingState.Charging)]
    public void ParsesLowBatteryAndPowerFlags(byte level, byte flags, ChargingState expected)
    {
        var frame = Sample();
        frame[12] = level;
        frame[13] = flags;
        Assert.True(HauteCodec.TryParse(frame, out var percent, out var charging));
        Assert.Equal(level, percent);
        Assert.Equal(expected, charging);
    }

    [Fact]
    public void DisconnectedControllerDoesNotBecomeZeroPercent()
    {
        var frame = Sample();
        frame[12] = 0xFF;
        Assert.True(HauteCodec.TryParse(frame, out var percent, out var charging));
        Assert.Null(percent);
        Assert.Equal(ChargingState.Unknown, charging);
    }

    [Fact]
    public void RejectsWrongReportTruncatedAndOutOfRangeValues()
    {
        var frame = Sample();
        Assert.False(HauteCodec.TryParse(frame[..13], out _, out _));
        Assert.False(HauteCodec.TryParse([], out _, out _));
        frame[12] = 101;
        Assert.False(HauteCodec.TryParse(frame, out _, out _));
        frame[12] = 70;
        frame[0] = 0xF8;
        Assert.False(HauteCodec.TryParse(frame, out _, out _));
    }
}
