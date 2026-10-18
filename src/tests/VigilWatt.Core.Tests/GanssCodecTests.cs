using VigilWatt.Core.Plugins.Ganss;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： GANSS 抓包样本、边界与接口隔离的回归测试。
/// 2026/10/18 20:20:00 路泽
/// </summary>
public sealed class GanssCodecTests
{
    private const string Path = @"\\?\hid#vid_05ac&pid_024f&mi_03#test";

    [Fact]
    public void QueryMatchesCapturedOfficialRequest()
    {
        var expected = Convert.FromHexString("002001000000000000000000000000000000000000000000000000000000000021");
        Assert.Equal(expected, GanssCodec.CreateQuery());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedResponseIs97Percent(bool hidPrefix)
    {
        var payload = Convert.FromHexString("2001006100000000000000000000000000000000000000000000000000000082");
        var input = hidPrefix ? new byte[] { 0 }.Concat(payload).ToArray() : payload;
        Assert.True(GanssCodec.TryParse(input, out int percent));
        Assert.Equal(97, percent);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(100, true)]
    [InlineData(0, false)]
    [InlineData(101, false)]
    [InlineData(255, false)]
    public void PercentMustBeNonzeroAndAtMost100(int percent, bool valid)
    {
        var input = Response(percent);
        Assert.Equal(valid, GanssCodec.TryParse(input, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(31)]
    public void CorruptPacketsAreRejected(int offset)
    {
        var input = Response(97);
        input[offset] ^= 1;
        Assert.False(GanssCodec.TryParse(input, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(34)]
    [InlineData(64)]
    public void WrongLengthsAreRejected(int length)
        => Assert.False(GanssCodec.TryParse(new byte[length], out _));

    [Fact]
    public void WrongHidReportIdIsRejected()
    {
        var report = new byte[] { 1 }.Concat(Response(97)).ToArray();
        Assert.False(GanssCodec.TryParse(report, out _));
    }

    [Theory]
    [InlineData(0x05AC, 0x024F, "&mi_03#", 0xFF60, 0x61, 33, 33, 0, true)]
    [InlineData(0x05AC, 0x024F, "&MI_03#", 0xFF60, 0x61, 33, 33, 0, true)]
    [InlineData(0x05AC, 0x024F, "&mi_00#", 0xFF60, 0x61, 33, 33, 0, false)]
    [InlineData(0x05AC, 0x024F, "&mi_04#", 0xFF60, 0x61, 33, 33, 0, false)]
    [InlineData(0x05AC, 0x024F, "&mi_030#", 0xFF60, 0x61, 33, 33, 0, false)]
    [InlineData(0x05AC, 0x0256, "&mi_03#", 0xFF60, 0x61, 33, 33, 0, false)]
    [InlineData(0x046D, 0x024F, "&mi_03#", 0xFF60, 0x61, 33, 33, 0, false)]
    [InlineData(0x05AC, 0x024F, "&mi_03#", 1, 0x61, 33, 33, 0, false)]
    [InlineData(0x05AC, 0x024F, "&mi_03#", 0xFF60, 1, 33, 33, 0, false)]
    [InlineData(0x05AC, 0x024F, "&mi_03#", 0xFF60, 0x61, 64, 33, 0, false)]
    [InlineData(0x05AC, 0x024F, "&mi_03#", 0xFF60, 0x61, 33, 64, 0, false)]
    [InlineData(0x05AC, 0x024F, "&mi_03#", 0xFF60, 0x61, 33, 33, 1, false)]
    public void OnlyCapturedVendorInterfaceIsEligible(ushort vid, ushort pid, string segment,
        ushort page, ushort usage, ushort input, ushort output, byte id, bool eligible)
        => Assert.Equal(eligible, GanssCodec.IsSupportedInterface(vid, pid,
            Path.Replace("&mi_03#", segment), page, usage, input, output, id));

    [Fact]
    public void UnknownReportIdIsRejected()
        => Assert.False(GanssCodec.IsSupportedInterface(0x05AC, 0x024F, Path, 0xFF60, 0x61, 33, 33, null));

    private static byte[] Response(int percent)
    {
        var report = new byte[32];
        report[0] = 0x20;
        report[1] = 1;
        report[3] = (byte)percent;
        report[^1] = (byte)(0x21 + percent);
        return report;
    }
}
