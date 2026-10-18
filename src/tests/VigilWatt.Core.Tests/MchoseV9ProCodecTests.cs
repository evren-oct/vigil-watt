using VigilWatt.Core.Plugins.Mchose;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： V9 Pro 官方抓包与接口隔离的回归测试。
/// 2026/10/19 00:32:00 路泽
/// </summary>
public sealed class MchoseV9ProCodecTests
{
    private const string Path = @"\\?\hid#vid_291d&pid_385d&mi_00&col05#test";

    [Fact]
    public void QueryExactlyMatchesOfficialCapture()
    {
        var expected = Convert.FromHexString("55650100000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000");
        Assert.Equal(expected, MchoseV9ProCodec.CreateQuery());
    }

    [Fact]
    public void CapturedResponseMatchesOfficial100Percent()
    {
        var response = Convert.FromHexString("55656402000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000");
        Assert.True(MchoseV9ProCodec.TryParse(response, out int percent));
        Assert.Equal(100, percent);
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
        var response = Response();
        response[2] = (byte)value;
        Assert.Equal(accepted, MchoseV9ProCodec.TryParse(response, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(128)]
    public void WrongLengthIsRejected(int length)
        => Assert.False(MchoseV9ProCodec.TryParse(new byte[length], out _));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void WrongSignatureIsRejected(int index)
    {
        var response = Response();
        response[index] ^= 1;
        Assert.False(MchoseV9ProCodec.TryParse(response, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(255)]
    public void UnknownStatusDoesNotChangeBattery(int flag)
    {
        var response = Response();
        response[3] = (byte)flag;
        Assert.True(MchoseV9ProCodec.TryParse(response, out int percent));
        Assert.Equal(100, percent);
    }

    [Fact]
    public void DoesNotAcceptGanssZeroReportPrefix()
        => Assert.False(MchoseV9ProCodec.TryParse(new byte[] { 0 }.Concat(Response()).ToArray(), out _));

    [Theory]
    [InlineData(0x291D, 0x385D, "&mi_00&col05#", 0xFF90, 1, 64, 64, 0, 0x55, true)]
    [InlineData(0x291D, 0x385D, "&MI_00&COL05#", 0xFF90, 1, 64, 64, 0, 0x55, true)]
    [InlineData(0x291D, 0x385D, "&mi_00&col01#", 0xFF90, 1, 64, 64, 0, 0x55, false)]
    [InlineData(0x291D, 0x385D, "&mi_00&col06#", 0xFF90, 1, 64, 64, 0, 0x55, false)]
    [InlineData(0x291D, 0x385D, "&mi_01&col05#", 0xFF90, 1, 64, 64, 0, 0x55, false)]
    [InlineData(0x291D, 0x385D, "&mi_00&col050#", 0xFF90, 1, 64, 64, 0, 0x55, false)]
    [InlineData(0x291D, 0x385E, "&mi_00&col05#", 0xFF90, 1, 64, 64, 0, 0x55, false)]
    [InlineData(0x291E, 0x385D, "&mi_00&col05#", 0xFF90, 1, 64, 64, 0, 0x55, false)]
    [InlineData(0x291D, 0x385D, "&mi_00&col05#", 0xFF00, 1, 64, 64, 0, 0x55, false)]
    [InlineData(0x291D, 0x385D, "&mi_00&col05#", 0xFF90, 2, 64, 64, 0, 0x55, false)]
    [InlineData(0x291D, 0x385D, "&mi_00&col05#", 0xFF90, 1, 65, 64, 0, 0x55, false)]
    [InlineData(0x291D, 0x385D, "&mi_00&col05#", 0xFF90, 1, 64, 65, 0, 0x55, false)]
    [InlineData(0x291D, 0x385D, "&mi_00&col05#", 0xFF90, 1, 64, 64, 1, 0x55, false)]
    [InlineData(0x291D, 0x385D, "&mi_00&col05#", 0xFF90, 1, 64, 64, 0, 0, false)]
    public void OnlyVerifiedVendorCollectionIsEligible(ushort vid, ushort pid, string segment,
        ushort page, ushort usage, ushort input, ushort output, ushort feature, byte id, bool expected)
        => Assert.Equal(expected, MchoseV9ProCodec.IsSupportedInterface(vid, pid,
            Path.Replace("&mi_00&col05#", segment), page, usage, input, output, feature, id));

    [Fact]
    public void UnknownReportIdIsRejected()
        => Assert.False(MchoseV9ProCodec.IsSupportedInterface(0x291D, 0x385D,
            Path, 0xFF90, 1, 64, 64, 0, null));

    private static byte[] Response()
    {
        var response = new byte[64];
        response[0] = 0x55;
        response[1] = 0x65;
        response[2] = 100;
        response[3] = 2;
        return response;
    }
}
