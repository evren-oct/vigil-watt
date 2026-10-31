using VigilWatt.Core.Plugins.Langtu;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： LangtuT98Codec —— 狼途 T98 的 65 字节被动状态帧解析。
/// 2026/10/31 20:34:00 20211
/// 样本与字节含义见 docs/hardware/langtu.md §1、issue #40。
/// </summary>
public class LangtuT98CodecTests
{
    /// <summary>
    /// 按 issue #40 的实测帧构造 65 字节整帧：有效内容只有前 8 字节，其余补零。
    /// 原文只给了 `00 AA 55 CC 33 0E &lt;pct&gt; 01 …`，尾部未记录。
    /// </summary>
    private static byte[] Frame(byte percent)
    {
        var f = new byte[LangtuT98Codec.FrameLength];
        f[0] = 0x00;   // report ID
        f[1] = 0xAA;   // 同步头
        f[2] = 0x55;
        f[3] = 0xCC;
        f[4] = 0x33;
        f[5] = 0x0E;   // 命令字
        f[6] = percent;
        f[7] = 0x01;   // 实测恒为 0x01，不是充电位
        return f;
    }

    [Theory]
    [InlineData(78)]   // 静置基准值
    [InlineData(87)]   // 插线后爬升值
    [InlineData(92)]   // 插线 22 分钟后的慢速段
    [InlineData(100)]
    public void T1_实测电量样本逐条解析(byte percent)
    {
        Assert.True(LangtuT98Codec.TryParse(Frame(percent), out int parsed));
        Assert.Equal(percent, parsed);
    }

    [Fact]
    public void T2_插拔瞬间的FF哨兵必须丢弃()
    {
        // issue #40：插拔充电线瞬间发 0xFF，不丢会显示 255%
        Assert.False(LangtuT98Codec.TryParse(Frame(0xFF), out _));
    }

    [Fact]
    public void T3_同步头不对一律拒绝()
    {
        byte[] bad = Frame(78);
        bad[1] = 0xAB;
        Assert.False(LangtuT98Codec.TryParse(bad, out _));
    }

    [Fact]
    public void T4_命令字不对一律拒绝()
    {
        // 同一接收器的 MI_03 通知集合也是 AA 55 头，只有命令字能分开
        byte[] bad = Frame(78);
        bad[5] = 0x0F;
        Assert.False(LangtuT98Codec.TryParse(bad, out _));
    }

    [Fact]
    public void T5_超出一百的读数不采信()
    {
        Assert.False(LangtuT98Codec.TryParse(Frame(101), out _));
    }

    [Fact]
    public void T6_截断帧不越界()
    {
        byte[] shortFrame = Frame(78)[..6];
        Assert.False(LangtuT98Codec.TryParse(shortFrame, out _));
        Assert.False(LangtuT98Codec.TryParse(Array.Empty<byte>(), out _));
    }

    [Fact]
    public void T7_reportID被剥掉的64字节形态同样可解析()
    {
        // 部分读路径会把 report ID 0 去掉，同步头前移到下标 0
        byte[] stripped = Frame(78)[1..];
        Assert.True(LangtuT98Codec.TryParse(stripped, out int parsed));
        Assert.Equal(78, parsed);
    }
}
