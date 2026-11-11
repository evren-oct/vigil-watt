using VigilWatt.Core.Plugins.Ilovbee;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： IlovbeeCodec —— ilovbee b98 的 20 字节被动状态帧解析。
/// 2026/11/01 17:32:05 20211
/// 样本逐帧取自 issue #68 的采集包，字节含义见 docs/hardware/devices/ilovbee.md §1。
/// </summary>
public class IlovbeeCodecTests
{
    /// <summary>按实测帧构造：`13 0A 01 00 04 &lt;事件&gt; &lt;电量&gt; &lt;标志&gt; … &lt;和校验&gt;`。</summary>
    private static byte[] Frame(byte evt, byte value, byte flags = 0)
    {
        var f = new byte[IlovbeeCodec.FrameLength];
        f[0] = 0x13;
        f[1] = 0x0A;
        f[2] = 0x01;
        f[3] = 0x00;
        f[4] = 0x04;
        f[5] = evt;
        f[6] = value;
        f[7] = flags;
        int sum = 0;
        for (int i = 0; i < 19; i++) sum += f[i];
        f[19] = (byte)sum;
        return f;
    }

    [Theory]
    [InlineData(75)]   // 105.52s 静置基准
    [InlineData(76)]   // 125.75s 插线后
    [InlineData(81)]   // 150.97s 开机回读
    [InlineData(100)]
    public void T1_实测电量样本逐条解析(byte percent)
    {
        Assert.True(IlovbeeCodec.TryParse(Frame(0x05, percent), out var kind, out int parsed, out bool edge));
        Assert.Equal(IlovbeeCodec.FrameKind.Battery, kind);
        Assert.Equal(percent, parsed);
        Assert.False(edge);
    }

    [Fact]
    public void T2_关机帧不是0电量_必须单独识别()
    {
        // issue #68：`13 0A 01 00 04 02 00 … 24` 是关机，[6] 的 0 不是电量
        Assert.True(IlovbeeCodec.TryParse(Frame(0x02, 0x00), out var kind, out _, out _));
        Assert.Equal(IlovbeeCodec.FrameKind.PowerOff, kind);
    }

    [Fact]
    public void T3_开机帧不是1电量()
    {
        Assert.True(IlovbeeCodec.TryParse(Frame(0x02, 0x01), out var kind, out _, out _));
        Assert.Equal(IlovbeeCodec.FrameKind.PowerOn, kind);
    }

    [Fact]
    public void T4_充电插入帧要打上事件标记()
    {
        // 123.62s 实测：`13 0A 01 00 04 05 64 01 … 8C`，[6]=100 是占位值，实际 75%
        Assert.True(IlovbeeCodec.TryParse(Frame(0x05, 100, flags: 0x01), out _, out int parsed, out bool edge));
        Assert.Equal(100, parsed);
        Assert.True(edge);
    }

    [Fact]
    public void T5_校验和错一字节一律拒绝()
    {
        byte[] bad = Frame(0x05, 75);
        bad[19] ^= 0xFF;
        Assert.False(IlovbeeCodec.TryParse(bad, out _, out _, out _));
    }

    [Fact]
    public void T6_reportId不对一律拒绝()
    {
        byte[] bad = Frame(0x05, 75);
        bad[0] = 0x12;
        Assert.False(IlovbeeCodec.TryParse(bad, out _, out _, out _));
    }

    [Fact]
    public void T7_未知事件字一律拒绝()
    {
        Assert.False(IlovbeeCodec.TryParse(Frame(0x07, 75), out _, out _, out _));
    }

    [Theory]
    [InlineData(101)]
    [InlineData(0xFF)]   // 插拔瞬间可能出现的哨兵
    public void T8_电量越界一律拒绝(byte percent)
    {
        Assert.False(IlovbeeCodec.TryParse(Frame(0x05, percent), out _, out _, out _));
    }

    [Fact]
    public void T9_短帧一律拒绝()
    {
        byte[] shortFrame = new byte[IlovbeeCodec.FrameLength - 1];
        Assert.False(IlovbeeCodec.TryParse(shortFrame, out _, out _, out _));
    }
}
