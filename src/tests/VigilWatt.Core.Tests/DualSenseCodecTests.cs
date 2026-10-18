using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Sony;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： DualSense 实测样本、CRC、电量状态与蓝牙接口隔离测试。
/// 2026/10/19 01:13:49 路泽
/// </summary>
public sealed class DualSenseCodecTests
{
    private const string Sample = "31D18082818500000108000000771EBCD200000E00FFFFBAFFFF1E54053B3A93250E8161862E800000008109090000000000E3429325010000B4DD79D1FD2D37C2000000000000000000B1094C4E";
    private const string BtPath = @"\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0ce6#sample";

    [Fact]
    public void CapturedReportIsEstimated15Discharging()
    {
        Assert.True(DualSenseCodec.TryParse(Convert.FromHexString(Sample), out int percent, out var state));
        Assert.Equal(15, percent);
        Assert.Equal(ChargingState.Discharging, state);
    }

    [Theory]
    [InlineData(0x00, "F7322B2B", true, 5, ChargingState.Discharging)]
    [InlineData(0x01, "B1094C4E", true, 15, ChargingState.Discharging)]
    [InlineData(0x09, "03DF97D3", true, 95, ChargingState.Discharging)]
    [InlineData(0x0A, "C9923E7C", true, 100, ChargingState.Discharging)]
    [InlineData(0x10, "D299EDCB", true, 5, ChargingState.Charging)]
    [InlineData(0x11, "94A28AAE", true, 15, ChargingState.Charging)]
    [InlineData(0x1A, "EC39F89C", true, 100, ChargingState.Charging)]
    [InlineData(0x20, "FC62D731", true, 100, ChargingState.Full)]
    [InlineData(0x2A, "C2C2C266", true, 100, ChargingState.Full)]
    [InlineData(0x0B, "8FA95919", false, 0, ChargingState.Unknown)]
    [InlineData(0x0F, "D642B457", false, 0, ChargingState.Unknown)]
    [InlineData(0x1B, "AA029FF9", false, 0, ChargingState.Unknown)]
    [InlineData(0x2F, "DD12484D", false, 0, ChargingState.Unknown)]
    [InlineData(0x30, "D9C911D1", false, 0, ChargingState.Unknown)]
    [InlineData(0xA0, "D022265A", false, 0, ChargingState.Unknown)]
    [InlineData(0xB0, "F589E0BA", false, 0, ChargingState.Unknown)]
    [InlineData(0xF0, "E329188F", false, 0, ChargingState.Unknown)]
    public void StatusAndRangeAreValidated(int raw, string crc, bool valid, int expected, ChargingState state)
    {
        var bytes = Convert.FromHexString(Sample);
        bytes[54] = (byte)raw;
        Convert.FromHexString(crc).CopyTo(bytes, 74);
        Assert.Equal(valid, DualSenseCodec.TryParse(bytes, out int percent, out var actual));
        Assert.Equal(expected, percent);
        Assert.Equal(state, actual);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(54)]
    [InlineData(73)]
    [InlineData(74)]
    [InlineData(77)]
    public void CorruptedFrameIsRejected(int offset)
    {
        var bytes = Convert.FromHexString(Sample);
        bytes[offset] ^= 1;
        Assert.False(DualSenseCodec.TryParse(bytes, out _, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(77)]
    [InlineData(79)]
    [InlineData(547)]
    public void WrongLengthIsRejected(int length)
        => Assert.False(DualSenseCodec.TryParse(new byte[length], out _, out _));

    [Theory]
    [InlineData(0x054C, 0x0CE6, 1, 5, 78, true)]
    [InlineData(0x054C, 0x0DF2, 1, 5, 78, false)]
    [InlineData(0x054D, 0x0CE6, 1, 5, 78, false)]
    [InlineData(0x054C, 0x0CE6, 1, 4, 78, false)]
    [InlineData(0x054C, 0x0CE6, 0xFF00, 5, 78, false)]
    [InlineData(0x054C, 0x0CE6, 1, 5, 64, false)]
    public void OnlyVerifiedInterfaceIsSelected(int vid, int pid, int page, int usage, int length, bool expected)
        => Assert.Equal(expected, DualSenseCodec.IsSupportedInterface((ushort)vid, (ushort)pid, BtPath,
            (ushort)page, (ushort)usage, (ushort)length));

    [Fact]
    public void UsbAndVirtualInterfacesAreExcluded()
    {
        Assert.False(DualSenseCodec.IsSupportedInterface(0x054C, 0x0CE6, @"\?\hid#vid_054c&pid_0ce6", 1, 5, 78));
        Assert.False(DualSenseCodec.IsSupportedInterface(0x054C, 0x0CE6, "virtual", 1, 5, 78));
        Assert.True(DualSenseCodec.IsSupportedInterface(0x054C, 0x0CE6, BtPath.ToUpperInvariant(), 1, 5, 78));
    }
}
