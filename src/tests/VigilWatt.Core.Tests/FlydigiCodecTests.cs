using VigilWatt.Core.Plugins.Flydigi;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 飞智 NewXInput 心跳帧、实测应答与接口判据的回归测试。
/// 2026/10/22 21:50:00 xliu
/// </summary>
public sealed class FlydigiCodecTests
{
    /// <summary>2026-10-19 21:41 实测应答：未充电，idx11=0x02 → 40%。</summary>
    private const string Captured40 =
        "045AA50101008002000000000245010070292130452500000128000011311F00";

    /// <summary>同一台设备插上充电线后的应答：idx11 变 0x11，其余 31 字节不变。</summary>
    private const string CapturedCharging =
        "045AA50101008002000000001145010070292130452500000128000011311F00";

    [Fact]
    public void HeartbeatMatchesCapturedOfficialRequest()
    {
        var frame = FlydigiCodec.CreateHeartbeat(0x03, 32);

        Assert.Equal(32, frame.Length);
        Assert.Equal("035AA50102030000", Convert.ToHexString(frame[..8]));
        Assert.All(frame[6..], b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedResponseIs40Percent(bool withReportId)
    {
        var frame = Convert.FromHexString(Captured40);
        var input = withReportId ? frame : frame[1..];

        Assert.True(FlydigiCodec.TryParse(input, out var status));
        Assert.Equal(40, status.Percent);
        Assert.Equal(0x02, status.RawLevel);
        Assert.False(status.IsCharging);
    }

    [Fact]
    public void ChargingReplacesLevelWith0x11()
    {
        Assert.True(FlydigiCodec.TryParse(Convert.FromHexString(CapturedCharging), out var status));

        Assert.True(status.IsCharging);
        Assert.Null(status.Percent);          // 充电时该字节不含电量，不得换算成 340% 或 20%
        Assert.Equal(0x11, status.RawLevel);
    }

    [Fact]
    public void TrapByteAtIdx25IsNotBattery()
    {
        // 实测帧 idx25 恒为 0x28（40），是陷阱；档位只认 idx11
        var frame = Convert.FromHexString(Captured40);
        Assert.Equal(0x28, frame[25]);

        Assert.True(FlydigiCodec.TryParse(frame, out var status));
        Assert.Equal(40, status.Percent);
    }

    [Fact]
    public void OtherCommandFramesAreRejected()
    {
        // 2026-10-19 22:00:59 实测的 cmd 0x52 帧：插件必须丢弃
        var cmd52 = new byte[32];
        cmd52[0] = 0x04;
        cmd52[1] = 0x5A;
        cmd52[2] = 0xA5;
        cmd52[3] = 0x52;
        cmd52[12] = 0x32;   // 这个位置在本帧里是别的东西，不能被当档位
        Assert.False(FlydigiCodec.TryParse(cmd52, out _));
    }

    [Theory]
    [InlineData(0x06)]
    [InlineData(0x28)]
    [InlineData(0xFF)]
    public void LevelsOutside0To5AreRejected(byte level)
    {
        var frame = Convert.FromHexString(Captured40);
        frame[12] = level;
        Assert.False(FlydigiCodec.TryParse(frame, out _));
    }

    /// <summary>档位 0 = 0%，编解码照实返回；"低于 2% 判无效"是基类过滤器的职责，不在这里拦。</summary>
    [Fact]
    public void Level0IsZeroPercent()
    {
        var frame = Convert.FromHexString(Captured40);
        frame[12] = 0x00;

        Assert.True(FlydigiCodec.TryParse(frame, out var status));
        Assert.Equal(0, status.Percent);
        Assert.False(status.IsCharging);
    }

    [Fact]
    public void ShortFrameIsRejected()
        => Assert.False(FlydigiCodec.TryParse(Convert.FromHexString("045AA501"), out _));

    [Theory]
    [InlineData(0x37D7, 0x2501, 0xFFA0, 0x0001, 32, 32, true)]
    [InlineData(0x37D7, 0x2501, 0xFFEE, 0x0001, 32, 32, false)]   // 同一 dongle 的另一个集合
    [InlineData(0x37D7, 0x2501, 0xFFA0, 0x0002, 32, 32, false)]
    [InlineData(0x37D7, 0x1003, 0xFFA0, 0x0001, 25, 25, false)]   // 蓝牙模式，电量字段未证实
    [InlineData(0x37D7, 0x2501, 0xFFA0, 0x0001, 31, 32, false)]
    public void InterfaceCriteria(ushort vid, ushort pid, ushort usagePage, ushort usage,
        ushort inLen, ushort outLen, bool expected)
        => Assert.Equal(expected, FlydigiCodec.IsSupportedInterface(vid, pid, usagePage, usage, inLen, outLen));
}
