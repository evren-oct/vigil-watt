using VigilWatt.Core.Plugins.Razer;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 验证雷蛇实机样本、查询白名单及损坏响应拒绝。
/// 2026/10/24 19:30:14 Mzood
/// </summary>
public class RazerViperSeCodecTests
{
    internal static byte[] CapturedBattery() =>
        Convert.FromHexString("00021F000000020780009C" + new string('0', 156) + "1900");

    internal static byte[] CapturedDischarging() =>
        Convert.FromHexString("00021F000000020784" + new string('0', 160) + "8100");

    internal static byte[] CapturedViperV3ProBattery() =>
        Convert.FromHexString("00021F00000002078000C5" + new string('0', 156) + "4000");

    internal static void UpdateChecksum(byte[] report)
    {
        report[89] = 0;
        for (int i = 3; i < 89; i++) report[89] ^= report[i];
    }

    [Fact]
    public void CapturedBatteryReports61Percent()
    {
        Assert.Equal(91, CapturedBattery().Length);
        Assert.True(RazerViperSeCodec.TryParseBattery(CapturedBattery(), out int percent));
        Assert.Equal(61, percent);
        Assert.True(RazerViperSeCodec.TryParseCharging(CapturedDischarging(), out bool charging));
        Assert.False(charging);
    }

    [Fact]
    public void CapturedViperV3ProBatteryReports77Percent()
    {
        // 2026-11-18 真机（1532:00C1，MI_00，91 字节 feature），raw 0xC5 = 197/255。
        var report = CapturedViperV3ProBattery();
        Assert.Equal(91, report.Length);
        Assert.True(RazerViperSeCodec.TryParseBattery(report, out int percent));
        Assert.Equal(77, percent);
    }

    [Theory]
    [InlineData(0x80, 0x85)]
    [InlineData(0x84, 0x81)]
    public void QueryMatchesKnownProtocol(int command, int checksum)
    {
        var expected = new byte[90];
        expected[1] = 0x1F;
        expected[5] = 2;
        expected[6] = 7;
        expected[7] = (byte)command;
        expected[88] = (byte)checksum;
        Assert.Equal(expected, RazerViperSeCodec.CreateQuery((byte)command));
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x04)]
    [InlineData(0x05)]
    [InlineData(0x85)]
    public void CannotBuildConfigurationOrUnknownCommands(int command) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RazerViperSeCodec.CreateQuery((byte)command));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(3, 1)]
    [InlineData(128, 50)]
    [InlineData(156, 61)]
    [InlineData(254, 100)]
    [InlineData(255, 100)]
    public void ScalesEntireBatteryRange(int raw, int expected)
    {
        var report = CapturedBattery();
        report[10] = (byte)raw;
        UpdateChecksum(report);
        Assert.True(RazerViperSeCodec.TryParseBattery(report, out int percent));
        Assert.Equal(expected, percent);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(1, 3)]
    [InlineData(1, 4)]
    [InlineData(1, 5)]
    [InlineData(2, 0x3F)]
    [InlineData(3, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 1)]
    [InlineData(6, 1)]
    [InlineData(6, 80)]
    [InlineData(7, 3)]
    [InlineData(8, 0x84)]
    [InlineData(90, 1)]
    public void RejectsUnrelatedOrFailedResponsesEvenWithValidChecksum(int index, int value)
    {
        var report = CapturedBattery();
        report[index] = (byte)value;
        UpdateChecksum(report);
        Assert.False(RazerViperSeCodec.TryParseBattery(report, out _));
    }

    [Fact]
    public void RejectsTruncatedExtendedAndCorruptedReports()
    {
        Assert.False(RazerViperSeCodec.TryParseBattery([], out _));
        Assert.False(RazerViperSeCodec.TryParseBattery(CapturedBattery().AsSpan(1), out _));
        Assert.False(RazerViperSeCodec.TryParseBattery(new byte[92], out _));
        var corrupted = CapturedBattery();
        corrupted[10] ^= 1;
        Assert.False(RazerViperSeCodec.TryParseBattery(corrupted, out _));
        Assert.False(RazerViperSeCodec.TryParseBattery(CapturedDischarging(), out _));
        Assert.False(RazerViperSeCodec.TryParseCharging(CapturedBattery(), out _));
    }

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(1, true, true)]
    [InlineData(2, false, false)]
    [InlineData(255, false, false)]
    public void ChargingFlagOnlyAcceptsDocumentedValues(int flag, bool valid, bool expected)
    {
        var report = CapturedDischarging();
        report[10] = (byte)flag;
        UpdateChecksum(report);
        Assert.Equal(valid, RazerViperSeCodec.TryParseCharging(report, out bool charging));
        Assert.Equal(expected, charging);
    }
}
