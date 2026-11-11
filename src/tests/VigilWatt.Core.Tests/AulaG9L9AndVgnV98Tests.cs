using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Aula;
using VigilWatt.Core.Plugins.Vgn;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 狼蛛 G9 / L9 与 VGN V98 Pro 三条协议帧的解析回归（PR #31）。
/// 2026/10/25 00:00:00 xliu
/// 帧布局见 docs/hardware/brands/aula.md §3 §4、docs/hardware/brands/vgn.md §3；样本由贡献者提供，本机无设备。
/// </summary>
public class AulaG9L9AndVgnV98Tests
{
    [Fact]
    public void G9_ParsesLevelAndCharging()
    {
        var frame = new byte[64];
        frame[0] = 0xAB;
        frame[1] = 0x47;
        frame[2] = 0x4D;
        frame[3] = 0x86;
        frame[6] = 76;
        frame[7] = 0x02;

        Assert.True(AulaG9Codec.TryParse(frame, out int? percent, out var charging));
        Assert.Equal(76, percent);
        Assert.Equal(ChargingState.Charging, charging);
    }

    [Fact]
    public void G9_ZeroLevelAndZeroFlag_IsNoReadingNotEmpty()
    {
        var frame = new byte[64];
        frame[0] = 0xAB;
        frame[1] = 0x47;
        frame[2] = 0x4D;
        frame[3] = 0x86;

        Assert.True(AulaG9Codec.TryParse(frame, out int? percent, out var charging));
        Assert.Null(percent);
        Assert.Equal(ChargingState.Unknown, charging);
    }

    [Fact]
    public void G9_RejectsOtherFrames()
    {
        var frame = new byte[64];
        frame[0] = 0xAB;
        frame[1] = 0x47;
        frame[2] = 0x4D;
        frame[3] = 0x85;
        frame[6] = 50;

        Assert.False(AulaG9Codec.TryParse(frame, out _, out _));
    }

    [Fact]
    public void L9_QueryIsReportIdPlusStatusCommand()
    {
        var query = AulaL9Codec.BuildStatusQuery(64);

        Assert.Equal(64, query.Length);
        Assert.Equal(0x0B, query[0]);
        Assert.Equal(0x81, query[1]);
        Assert.All(query[2..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void L9_ParsesLevelAndCharging()
    {
        var frame = new byte[32];
        frame[0] = 0x0A;
        frame[1] = 0x81;
        frame[17] = 0x01;
        frame[18] = 64;

        Assert.True(AulaL9Codec.TryParse(frame, out int percent, out var charging));
        Assert.Equal(64, percent);
        Assert.Equal(ChargingState.Charging, charging);
    }

    [Fact]
    public void L9_RejectsLevelOver100()
    {
        var frame = new byte[32];
        frame[0] = 0x0A;
        frame[1] = 0x81;
        frame[18] = 120;

        Assert.False(AulaL9Codec.TryParse(frame, out _, out _));
    }

    [Fact]
    public void V98Pro_QueryStartsWithReportIdAndTag()
    {
        var query = VgnV98ProCodec.BuildQueryReport(17);

        Assert.Equal(17, query.Length);
        Assert.Equal(0x04, query[0]);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x1A, 0x06, 0x00, 0x00, 0x00 }, query[1..8]);
    }

    [Fact]
    public void V98Pro_ParsesLevelAndCharging()
    {
        var frame = new byte[17];
        frame[0] = 0x04;
        frame[3] = 0x1A;
        frame[8] = 88;
        frame[9] = 0x01;

        Assert.True(VgnV98ProCodec.TryParse(frame, out int percent, out var charging));
        Assert.Equal(88, percent);
        Assert.Equal(ChargingState.Charging, charging);
    }

    [Fact]
    public void V98Pro_IgnoresFramesWithoutTag()
    {
        var frame = new byte[17];
        frame[0] = 0x04;
        frame[3] = 0x1B;
        frame[8] = 88;

        Assert.False(VgnV98ProCodec.TryParse(frame, out _, out _));
    }
}
