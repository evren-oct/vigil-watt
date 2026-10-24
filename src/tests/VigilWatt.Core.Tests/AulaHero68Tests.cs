using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Aula;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： HERO 68 Air 真机帧、共享接收器身份及分片边界回归测试。
/// 2026/10/23 20:05:00 30149
/// </summary>
public class AulaHero68Tests
{
    private static byte[] UuidResponse() => Convert.FromHexString(
        "82010001000612000000000A" + new string('0', 100) + "50");

    private static byte[] BatteryResponse() => Convert.FromHexString(
        "8700000100020A00" + new string('0', 108) + "62");

    private static void FixChecksum(byte[] packet)
    {
        packet[^1] = 0;
        packet[^1] = (byte)(255 - ((packet.Sum(b => b) + AulaHero68Codec.ReportId) & 255));
    }

    private static byte[][] Fragments(byte[] packet)
        => Enumerable.Range(0, 5).Select(i => AulaHero68Codec.CreateFragment(
            packet.AsSpan(i * 14, Math.Min(14, packet.Length - i * 14)), 5, i + 1, (i + 6) & 7)).ToArray();

    [Fact]
    public void QueriesMatchOfficialBytesAndCapturedFirstFragment()
    {
        var uuid = AulaHero68Codec.CreateUuidQuery();
        Assert.Equal(64, uuid.Length);
        Assert.Equal("09820100010006", Convert.ToHexString(uuid[..7]));
        Assert.Equal(0x6C, uuid[^1]);
        Assert.Equal("096605018E09820100010006000000000000008D",
            Convert.ToHexString(AulaHero68Codec.CreateFragment(uuid.AsSpan(0, 14), 5, 1, 1)));
        var battery = AulaHero68Codec.CreateBatteryQuery();
        Assert.Equal("09870000010000", Convert.ToHexString(battery[..7]));
        Assert.Equal(0x6E, battery[^1]);
    }

    [Fact]
    public void OnlyOfficialReceiverCollectionMatches()
    {
        Assert.True(AulaHero68Codec.IsSupportedInterface(0x372E, 0x106C, 0xFF60, 0x61, 20, 20, 9));
        Assert.False(AulaHero68Codec.IsSupportedInterface(0x372E, 0x106C, 0xFF02, 2, 20, 20, 9));
        Assert.False(AulaHero68Codec.IsSupportedInterface(0x372E, 0x103E, 0xFF60, 0x61, 20, 20, 9));
        Assert.False(AulaHero68Codec.IsSupportedInterface(0x372E, 0x106C, 0xFF60, 0x61, 64, 64, 9));
        Assert.False(AulaHero68Codec.IsSupportedInterface(0x372E, 0x106C, 0xFF60, 0x61, 20, 20, null));
        Assert.False(AulaHero68Codec.IsSupportedInterface(0x372E, 0x106C, 0xFF60, 0x61, 20, 20, 8));
        Assert.False(AulaHero68Codec.IsSupportedInterface(0x3554, 0xFA09, 0xFF60, 0x61, 20, 20, 9));
    }

    [Theory]
    [InlineData(0x0A, true)]
    [InlineData(0x10, false)]
    [InlineData(0x15, false)]
    [InlineData(0x04, false)]
    public void SharedReceiverNeedsExactHero68AirUuid(byte model, bool expected)
    {
        var packet = UuidResponse();
        packet[11] = model;
        FixChecksum(packet);
        Assert.Equal(expected, AulaHero68Codec.IsHero68Air(packet));
    }

    [Fact]
    public void CapturedBatteryIsTenPercentDischarging()
    {
        Assert.True(AulaHero68Codec.TryParseBattery(BatteryResponse(), out int percent, out var charging));
        Assert.Equal(10, percent);
        Assert.Equal(ChargingState.Discharging, charging);
    }

    [Fact]
    public void CapturedChargingFrameKeepsPercentAndChangesState()
    {
        var packet = Convert.FromHexString("8700000100020A10" + new string('0', 108) + "52");
        Assert.True(AulaHero68Codec.TryParseBattery(packet, out int percent, out var charging));
        Assert.Equal(10, percent);
        Assert.Equal(ChargingState.Charging, charging);
    }

    [Theory]
    [InlineData(0, 0, ChargingState.Discharging)]
    [InlineData(100, 0, ChargingState.Discharging)]
    [InlineData(37, 0x10, ChargingState.Charging)]
    [InlineData(100, 1, ChargingState.Full)]
    [InlineData(100, 0x11, ChargingState.Charging)]
    public void BatteryFlagsFollowOfficialHighAndLowNibbles(byte percent, byte flag, ChargingState expected)
    {
        var packet = BatteryResponse();
        packet[6] = percent;
        packet[7] = flag;
        FixChecksum(packet);
        Assert.True(AulaHero68Codec.TryParseBattery(packet, out int actual, out var charging));
        Assert.Equal(percent, actual);
        Assert.Equal(expected, charging);
    }

    [Fact]
    public void BatteryRejectsCorruptionWrongHeadersPushAndInvalidPercent()
    {
        var packet = BatteryResponse();
        for (int i = 0; i < packet.Length; i++)
        {
            var corrupt = packet.ToArray();
            corrupt[i] ^= 1;
            Assert.False(AulaHero68Codec.TryParseBattery(corrupt, out _, out _));
        }
        foreach (int index in new[] { 0, 1, 2, 3, 4, 5 })
        {
            var wrongHeader = packet.ToArray();
            wrongHeader[index] ^= 1;
            FixChecksum(wrongHeader);
            Assert.False(AulaHero68Codec.TryParseBattery(wrongHeader, out _, out _));
        }
        packet[6] = 101;
        FixChecksum(packet);
        Assert.False(AulaHero68Codec.TryParseBattery(packet, out _, out _));
        Assert.False(AulaHero68Codec.TryParseBattery(UuidResponse(), out _, out _));
        Assert.False(AulaHero68Codec.TryParseBattery(new byte[64], out _, out _));
        var push = Convert.FromHexString("FE050A" + new string('0', 118) + "E9");
        Assert.False(AulaHero68Codec.TryParseBattery(push, out _, out _));
    }

    [Fact]
    public void CapturedAckMatchesExactFragmentAndSync()
    {
        var sent = Convert.FromHexString("096605018E09820100010006000000000000008D");
        var ack = Convert.FromHexString("0966050180EC0000000000000000000000000000");
        Assert.True(AulaHero68Codec.IsMatchingAck(ack, sent));
        Assert.Equal(ack, AulaHero68Codec.CreateAck(sent));
        var other = AulaHero68Codec.CreateFragment(new byte[14], 5, 1, 2);
        Assert.False(AulaHero68Codec.IsMatchingAck(ack, other));
        ack[5] ^= 1;
        Assert.False(AulaHero68Codec.IsMatchingAck(ack, sent));
    }

    [Fact]
    public void ReassemblesRealResponseAcrossSyncWrapAndIgnoresRetransmission()
    {
        var packets = Fragments(UuidResponse());
        var assembler = new AulaHero68PacketAssembler();
        for (int i = 0; i < 4; i++)
        {
            Assert.Null(assembler.Accept(packets[i]));
            Assert.Null(assembler.Accept(packets[i]));
        }
        Assert.Equal(UuidResponse(), assembler.Accept(packets[4]));
        Assert.Null(assembler.Accept(packets[4]));
        foreach (var frame in Fragments(BatteryResponse()).Take(4)) Assert.Null(assembler.Accept(frame));
        Assert.Equal(BatteryResponse(), assembler.Accept(Fragments(BatteryResponse())[4]));
    }

    [Fact]
    public void OutOfOrderOversizedTruncatedAndBadChecksumFramesCannotCompletePacket()
    {
        var frames = Fragments(BatteryResponse());
        var assembler = new AulaHero68PacketAssembler();
        Assert.Null(assembler.Accept(frames[0]));
        foreach (var frame in frames.Skip(2)) Assert.Null(assembler.Accept(frame));
        foreach (var frame in frames.Take(4)) Assert.Null(assembler.Accept(frame));
        var oversized = AulaHero68Codec.CreateFragment(new byte[14], 5, 5, 2);
        Assert.Null(assembler.Accept(oversized));
        for (int length = 0; length < 20; length++) Assert.Null(assembler.Accept(frames[0][..length]));
        frames[0][4] = 127;
        Assert.Null(assembler.Accept(frames[0]));
        Assert.False(AulaHero68Codec.TryParseFragment(frames[0], out _, out _, out _, out _));
    }

    [Fact]
    public void UnexpectedSyncCannotSplicePacketsAndNewFirstFragmentRecovers()
    {
        var frames = Fragments(BatteryResponse());
        var assembler = new AulaHero68PacketAssembler();
        Assert.Null(assembler.Accept(frames[0]));
        Assert.Null(assembler.Accept(AulaHero68Codec.CreateFragment(new byte[14], 5, 2, 3)));
        foreach (var frame in frames.Skip(2)) Assert.Null(assembler.Accept(frame));
        foreach (var frame in frames.Take(4)) Assert.Null(assembler.Accept(frame));
        Assert.Equal(BatteryResponse(), assembler.Accept(frames[4]));
    }
}
