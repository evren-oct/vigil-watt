using VigilWatt.Core.Plugins.Atk;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： ATK 键盘 VIA 读电量帧（命令回显保护）的回归测试。
/// 2026/10/20 10:30:00 MOSS
/// </summary>
public sealed class AtkViaCodecTests
{
    [Fact]
    public void QueryHasCorrectHeader()
    {
        var query = AtkViaCodec.CreateQuery(33);
        Assert.Equal(33, query.Length);
        Assert.Equal(new byte[] { 0x00, AtkViaCodec.CommandGetValue, AtkViaCodec.CustomId, AtkViaCodec.KeyBattery },
            query[..4]);
    }

    [Fact]
    public void CapturedResponseIs100PercentCharging()
    {
        // 00 02 96 24 64 01 … = 100% 充电中
        var response = Convert.FromHexString("000296246401000000000000000000000000000000000000000000000000000000");
        Assert.True(AtkViaCodec.TryParse(response, out int percent, out bool charging));
        Assert.Equal(100, percent);
        Assert.True(charging);
    }

    [Theory]
    [InlineData(2, 0, true)]
    [InlineData(50, 1, true)]
    [InlineData(100, 1, true)]
    [InlineData(1, 0, true)]
    [InlineData(0, 0, false)]
    [InlineData(101, 0, false)]
    [InlineData(255, 0, false)]
    public void PercentAndChargeBoundaries(int percent, byte charge, bool accepted)
    {
        var input = BuildResponse((byte)percent, charge);
        Assert.Equal(accepted, AtkViaCodec.TryParse(input, out _, out _));
    }

    /// <summary>键盘通道会把请求原样回显：回显帧的命令/通道/键都对，但电量字段是 0（占位），必须拒。</summary>
    [Fact]
    public void EchoOfRequestIsRejected()
        => Assert.False(AtkViaCodec.TryParse(AtkViaCodec.CreateQuery(33), out _, out _));

    [Fact]
    public void WrongCustomIdIsRejected()
    {
        var input = BuildResponse(80, 1);
        input[2] = 0x97;
        Assert.False(AtkViaCodec.TryParse(input, out _, out _));
    }

    [Theory]
    [InlineData(0x373B, 0xFF60, 5, 5, 0x00, true)]
    [InlineData(0x373B, 0xFF60, 33, 33, 0x00, true)]
    [InlineData(0x373B, 0xFF60, 4, 4, 0x00, false)]
    [InlineData(0x373B, 0xFF61, 5, 5, 0x00, false)]
    [InlineData(0x373C, 0xFF60, 5, 5, 0x00, false)]
    [InlineData(0x373B, 0xFF60, 5, 5, 0x01, false)]
    public void OnlyVerifiedViaInterfaceIsEligible(ushort vid, ushort page,
        ushort input, ushort output, byte id, bool expected)
        => Assert.Equal(expected, AtkViaCodec.IsSupportedInterface(vid, page, input, output, id));

    /// <summary>VIA 通道通常没有 report ID（0x00），描述符里读不到时按"未知"接受，不能因此丢掉键盘。</summary>
    [Fact]
    public void UnknownReportIdIsAcceptedForVia()
        => Assert.True(AtkViaCodec.IsSupportedInterface(0x373B, 0xFF60, 5, 5, null));

    private static byte[] BuildResponse(byte percent, byte charge)
    {
        var report = new byte[33];
        report[0] = AtkViaCodec.ReportId;
        report[1] = AtkViaCodec.CommandGetValue;
        report[2] = AtkViaCodec.CustomId;
        report[3] = AtkViaCodec.KeyBattery;
        report[4] = percent;
        report[5] = charge;
        return report;
    }
}
