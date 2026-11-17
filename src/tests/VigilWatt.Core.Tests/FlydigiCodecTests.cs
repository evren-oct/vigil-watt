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
    [InlineData(0x37D7, 0x2501, 0xFFA0, 0x0001, 32, 32, true)]   // APEX5：32/32
    [InlineData(0x37D7, 0x2502, 0xFFA0, 0x0001, 33, 33, true)]   // APEX6：本机实测 33/33
    [InlineData(0x37D7, 0x2502, 0xFFA0, 0x0001, 32, 32, false)]  // APEX6 用 32/32 判据会漏认（回归）
    [InlineData(0x37D7, 0x2501, 0xFFEE, 0x0001, 32, 32, false)]   // 同一 dongle 的另一个集合
    [InlineData(0x37D7, 0x2501, 0xFFA0, 0x0002, 32, 32, false)]
    [InlineData(0x37D7, 0x1003, 0xFFA0, 0x0001, 25, 25, false)]   // 蓝牙模式，电量字段未证实
    [InlineData(0x37D7, 0x2501, 0xFFA0, 0x0001, 31, 32, false)]
    public void InterfaceCriteria(ushort vid, ushort pid, ushort usagePage, ushort usage,
        ushort inLen, ushort outLen, bool expected)
        => Assert.Equal(expected, FlydigiCodec.IsSupportedInterface(vid, pid, usagePage, usage, inLen, outLen));

    // ------------------------------------------------------------------ APEX6（型号 ID 149/150/152 → 0~10 档 ×10）

    /// <summary>2026-11-17 真机（37D7:2502）：[5]=0x96=150、[11]=0x0A → 档位 10、未充电 → 100%。</summary>
    private const string Apex6Real =
        "5AA50101009601000000000A45010090760509011100000143000010639F0065";

    [Fact]
    public void Apex6RealResponseParsesAs100Percent()
    {
        var frame = Convert.FromHexString(Apex6Real);
        Assert.True(FlydigiCodec.TryParse(frame, out var status));
        Assert.Equal(100, status.Percent);
        Assert.Equal(0x0A, status.RawLevel);
        Assert.False(status.IsCharging);
    }

    [Fact]
    public void Apex6ChargingFlagInHighNibble()
    {
        // [11] = 0x16 → 高 4 位 1 = 充电中，低 4 位 6 = 档位 6 → 60%
        var frame = Convert.FromHexString(Apex6Real);
        frame[11] = 0x16;
        Assert.True(FlydigiCodec.TryParse(frame, out var status));
        Assert.Equal(60, status.Percent);
        Assert.True(status.IsCharging);
    }

    [Fact]
    public void Apex6ChargedStateIs100Percent()
    {
        // [11] = 0x25 → 高 4 位 2 = 已充满 → 100%
        var frame = Convert.FromHexString(Apex6Real);
        frame[11] = 0x25;
        Assert.True(FlydigiCodec.TryParse(frame, out var status));
        Assert.Equal(100, status.Percent);
        Assert.False(status.IsCharging);
    }

    [Fact]
    public void Apex6UndefinedStatusAndOverLevelRejected()
    {
        var badStatus = Convert.FromHexString(Apex6Real);
        badStatus[11] = 0x3A;   // 高 4 位 3 = 未定义
        Assert.False(FlydigiCodec.TryParse(badStatus, out _));

        var overLevel = Convert.FromHexString(Apex6Real);
        overLevel[11] = 0x0B;   // 低 4 位 11 = 超出 0~10 档
        Assert.False(FlydigiCodec.TryParse(overLevel, out _));
    }

    [Fact]
    public void Apex5AndApex6ShareFrameButBranchByDeviceId()
    {
        // APEX5 真机帧 [5]=0x80（APEX5），状态 0x02 → 40%（×20），不能被当成 APEX6 的 ×10
        var apex5 = Convert.FromHexString(Captured40);
        Assert.Equal(0x80, apex5[6]);   // 型号 ID 128
        Assert.True(FlydigiCodec.TryParse(apex5, out var s5));
        Assert.Equal(40, s5.Percent);
    }
}
