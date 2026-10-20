using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Razer;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 雷蛇 Kraken V4 查询帧、真机应答与错误帧的回归测试。
/// 2026/10/19 22:26:14 kaniu
/// 样本取自 Synapse 4 记录与真机应答（docs/hardware/protocols.md §16）。
/// </summary>
public class RazerCodecTests
{
    // 2026-10-19 22:24 真机：63% 放电。
    private const string BatteryReply63 =
        "020260000000050080802101013F0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000007900";

    // 2026-10-19 22:24 真机：未充电。
    private const string ChargingReply0 =
        "020260000000050080802A0101000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000004D00";

    [Fact]
    public void BatteryQueryMatchesOfficialFrame()
    {
        var frame = RazerCodec.BuildQuery(RazerCodec.GetBatteryStatus);
        Assert.Equal(64, frame.Length);
        Assert.Equal(Convert.FromHexString("02006000000004000080210000"), frame[..13]);
        Assert.Equal(0xC7, frame[62]);
        Assert.Equal(0, frame[63]);
    }

    [Fact]
    public void ChargingQueryOnlyChangesCommandAndChecksum()
    {
        var battery = RazerCodec.BuildQuery(RazerCodec.GetBatteryStatus);
        var charging = RazerCodec.BuildQuery(RazerCodec.GetChargingStatus);
        Assert.Equal(RazerCodec.GetChargingStatus, charging[10]);
        Assert.Equal(battery[..10], charging[..10]);
        Assert.Equal(0xCC, charging[62]);
    }

    [Theory]
    [InlineData(63, BatteryReply63)]
    [InlineData(100,
        "02026000000005008080210101640000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000002200")]
    public void ParsesRealBatteryReplies(int expected, string hex)
    {
        Assert.True(RazerCodec.TryParseBattery(Convert.FromHexString(hex), out int percent));
        Assert.Equal(expected, percent);
    }

    [Theory]
    [InlineData(0, ChargingReply0)]
    [InlineData(1,
        "020260000000050080802A0101010000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000004C00")]
    public void ParsesChargingReplies(byte expected, string hex)
    {
        Assert.True(RazerCodec.TryParseCharging(Convert.FromHexString(hex), out byte flag));
        Assert.Equal(expected, flag);
    }

    [Theory]
    [InlineData(0)]   // report ID
    [InlineData(1)]   // 状态字节
    [InlineData(2)]   // 事务 ID
    [InlineData(10)]  // 命令
    [InlineData(11)]  // 应答方向
    [InlineData(12)]  // 数据长度
    [InlineData(62)]  // 校验和
    public void RejectsAnyMismatchedHeaderByte(int offset)
    {
        var frame = Convert.FromHexString(BatteryReply63);
        frame[offset] ^= 0xFF;
        Assert.False(RazerCodec.TryParseBattery(frame, out _));
    }

    [Fact]
    public void RejectsOtherCommandsAndOutOfRangeValues()
    {
        var charging = Convert.FromHexString(ChargingReply0);
        Assert.False(RazerCodec.TryParseBattery(charging, out _));

        var over = Convert.FromHexString(BatteryReply63);
        over[13] = 101;
        over[62] = Checksum(over);
        Assert.False(RazerCodec.TryParseBattery(over, out _));

        var unknownCharging = Convert.FromHexString(ChargingReply0);
        unknownCharging[13] = 2;
        unknownCharging[62] = Checksum(unknownCharging);
        Assert.False(RazerCodec.TryParseCharging(unknownCharging, out _));
    }

    [Fact]
    public void RejectsTruncationAndEmptyFrames()
    {
        var frame = Convert.FromHexString(BatteryReply63);
        Assert.False(RazerCodec.TryParseBattery(frame[..63], out _));
        Assert.False(RazerCodec.TryParseBattery([], out _));
    }

    [Theory]
    [InlineData(1, 63, ChargingState.Charging)]
    [InlineData(0, 63, ChargingState.Discharging)]
    [InlineData(0, 100, ChargingState.Full)]
    [InlineData(2, 63, ChargingState.Unknown)]
    public void MapsChargingFlagWithSharedFullBatteryRule(byte flag, int percent, ChargingState expected)
        => Assert.Equal(expected, RazerCodec.ToChargingState(flag, percent));

    [Fact]
    public void UnknownPercentageKeepsChargingUnknown()
        => Assert.Equal(ChargingState.Unknown, RazerCodec.ToChargingState(0, null));

    /// <summary>应答校验和从 [1] 起算到 [61]（不含报告 ID）。</summary>
    private static byte Checksum(byte[] frame)
    {
        byte crc = 0;
        for (int i = 1; i < 62; i++) crc ^= frame[i];
        return crc;
    }
}
