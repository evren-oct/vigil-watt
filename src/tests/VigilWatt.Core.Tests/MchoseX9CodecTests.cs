using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Mchose;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： X9 官方抓包样本、状态字节映射、边界与接口隔离的回归测试。
/// 2026/10/20 10:30:00 MOSS
/// </summary>
public sealed class MchoseX9CodecTests
{
    private const string Path = @"\\?\hid#vid_3837&pid_6045&mi_00&col05#test";

    [Fact]
    public void QueryMatchesV9FamilyCapture()
    {
        // 与 V9 Pro 同帧结构：55 65 01 + 61 个补零
        var expected = Convert.FromHexString("55650100000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000");
        Assert.Equal(expected, MchoseX9Codec.CreateQuery());
    }

    [Theory]
    [InlineData(0x02, ChargingState.Discharging)]
    [InlineData(0x03, ChargingState.Charging)]
    [InlineData(0x04, ChargingState.Full)]
    [InlineData(0x1A, ChargingState.Discharging)]
    [InlineData(0x00, ChargingState.Unknown)]
    [InlineData(0xFF, ChargingState.Unknown)]
    public void StatusByteMapsToChargingState(byte status, ChargingState expected)
    {
        var response = Response(percent: 80, status);
        Assert.True(MchoseX9Codec.TryParse(response, out int percent, out var charging));
        Assert.Equal(80, percent);
        Assert.Equal(expected, charging);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(50, true)]
    [InlineData(100, true)]
    [InlineData(0, false)]
    [InlineData(101, false)]
    [InlineData(255, false)]
    public void InvalidPercentIsRejected(int value, bool accepted)
    {
        var response = Response(100, 0x02);
        response[2] = (byte)value;
        Assert.Equal(accepted, MchoseX9Codec.TryParse(response, out _, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(128)]
    public void WrongLengthIsRejected(int length)
        => Assert.False(MchoseX9Codec.TryParse(new byte[length], out _, out _));

    [Fact]
    public void EchoOfOwnQueryIsRejected()
        => Assert.False(MchoseX9Codec.TryParse(MchoseX9Codec.CreateQuery(), out _, out _));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void WrongSignatureIsRejected(int index)
    {
        var response = Response(80, 0x02);
        response[index] ^= 1;
        Assert.False(MchoseX9Codec.TryParse(response, out _, out _));
    }

    [Theory]
    [InlineData(0x3837, 0x6045, "&mi_00&col05#", 0xFF90, 1, 64, 64, 0, 0x55, true)]
    [InlineData(0x3837, 0x6045, "&MI_00&COL05#", 0xFF90, 1, 64, 64, 0, 0x55, true)]
    [InlineData(0x3837, 0x6045, "&mi_00&col06#", 0xFF90, 1, 64, 64, 0, 0x55, false)]
    [InlineData(0x3837, 0x6045, "&mi_01&col05#", 0xFF90, 1, 64, 64, 0, 0x55, false)]
    [InlineData(0x3837, 0x6046, "&mi_00&col05#", 0xFF90, 1, 64, 64, 0, 0x55, false)]
    [InlineData(0x3838, 0x6045, "&mi_00&col05#", 0xFF90, 1, 64, 64, 0, 0x55, false)]
    [InlineData(0x3837, 0x6045, "&mi_00&col05#", 0xFF00, 1, 64, 64, 0, 0x55, false)]
    [InlineData(0x3837, 0x6045, "&mi_00&col05#", 0xFF90, 2, 64, 64, 0, 0x55, false)]
    [InlineData(0x3837, 0x6045, "&mi_00&col05#", 0xFF90, 1, 65, 64, 0, 0x55, false)]
    [InlineData(0x3837, 0x6045, "&mi_00&col05#", 0xFF90, 1, 64, 65, 0, 0x55, false)]
    [InlineData(0x3837, 0x6045, "&mi_00&col05#", 0xFF90, 1, 64, 64, 1, 0x55, false)]
    [InlineData(0x3837, 0x6045, "&mi_00&col05#", 0xFF90, 1, 64, 64, 0, 0, false)]
    public void OnlyVerifiedVendorCollectionIsEligible(ushort vid, ushort pid, string segment,
        ushort page, ushort usage, ushort input, ushort output, ushort feature, byte id, bool expected)
        => Assert.Equal(expected, MchoseX9Codec.IsSupportedInterface(vid, pid,
            Path.Replace("&mi_00&col05#", segment), page, usage, input, output, feature, id));

    [Fact]
    public void UnknownReportIdIsRejected()
        => Assert.False(MchoseX9Codec.IsSupportedInterface(0x3837, 0x6045,
            Path, 0xFF90, 1, 64, 64, 0, null));

    /// <summary>
    /// X9 与键鼠同号段（VID 0x3837）：分类器必须把它排在鼠标分支之前，
    /// 否则会往耳机发 0x11/0x06 触发帧、或把查询帧发到鼠标接收器。
    /// </summary>
    [Theory]
    [InlineData(0x3837, 0x6045, "&mi_00&col05#", 0xFF90, 1, 64, 64, 0, 0x55, MchoseInterfaceKind.HeadsetX9)]
    [InlineData(0x3837, 0x201B, "&mi_01&col01#", 0xFF02, 0x0002, 20, 20, 0, 0x13, MchoseInterfaceKind.KeyboardV1)]
    [InlineData(0x3837, 0x100A, "&mi_02&col03#", 0xFF00, 0x0002, 65, 65, 0, 0x11, MchoseInterfaceKind.MouseV2)]
    public void ClassifierRoutesX9BeforeMouse(ushort vid, ushort pid, string segment,
        ushort page, ushort usage, ushort input, ushort output, ushort feature, byte id, MchoseInterfaceKind expected)
        => Assert.Equal(expected, MchoseInterfaceClassifier.Classify(vid, pid,
            Path.Replace("&mi_00&col05#", segment), page, usage, input, output, feature, id));

    [Fact]
    public void X9InterfaceIsNotMistakenForHeadsetV9()
        => Assert.NotEqual(MchoseInterfaceKind.HeadsetV9,
            MchoseInterfaceClassifier.Classify(0x3837, 0x6045, Path, 0xFF90, 1, 64, 64, 0, 0x55));

    private static byte[] Response(int percent, byte status)
    {
        var response = new byte[64];
        response[0] = 0x55;
        response[1] = 0x65;
        response[2] = (byte)percent;
        response[3] = status;
        return response;
    }
}
