using VigilWatt.Core.Plugins.Eweadn;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： EweadnMouseCodec —— 前行者 S9 鼠标贝盈协议 feature report 0x03 电量帧（63 字节）。
/// 2026/10/18 13:40:00 Admin
/// 帧结构与实测依据见 docs/hardware/brands/eweadn.md §1.2。
/// </summary>
public class EweadnMouseCodecTests
{
    private static byte[] Hex(string s) => Convert.FromHexString(s.Replace(" ", ""));

    /// <summary>真机实测响应（73% 未充电）：03 50 00 0B 02 01 49 21 07 01 90 65 04 A5 5A 00。</summary>
    private static readonly byte[] MeasuredResponse =
        Hex("03 50 00 0B 02 01 49 21 07 01 90 65 04 A5 5A 00");

    [Fact]
    public void M1_查询帧为63字节且命令头正确()
    {
        byte[] frame = EweadnMouseCodec.BuildBasicsQuery();

        Assert.Equal(63, frame.Length);
        Assert.Equal((byte)0x50, frame[1]);
        Assert.Equal((byte)0x00, frame[2]);
        Assert.Equal((byte)0x02, frame[3]);
        Assert.Equal((byte)0x4F, frame[4]);
        Assert.Equal((byte)0x81, frame[5]);
    }

    [Fact]
    public void M2_查询帧CRC为0x22()
    {
        // 0x50+0x00+0x02+0x4F+0x81 = 0x122 → 低 8 位 0x22
        byte[] frame = EweadnMouseCodec.BuildBasicsQuery();
        Assert.Equal((byte)0x22, frame[0]);
        Assert.Equal(EweadnMouseCodec.Checksum(frame), frame[0]);
    }

    [Fact]
    public void M3_解析实测响应的电量()
    {
        var parsed = EweadnMouseCodec.ParseBasics(MeasuredResponse);

        Assert.NotNull(parsed);
        Assert.Equal(73, parsed!.Value.Percent);
        Assert.False(parsed.Value.Charging);
    }

    [Fact]
    public void M4_交叉验证DPI字段()
    {
        // 官方 decodeGetBasics：maxDpi = t[10] + t[11] * 255 = 0x90 + 0x65 * 255 = 25899
        // 与 S9 Pro 的 26000 DPI 规格吻合——证明偏移解析正确，不是巧合。
        Assert.Equal(25899, MeasuredResponse[10] + MeasuredResponse[11] * 255);
    }

    [Fact]
    public void M5_bit7为充电标志()
    {
        byte[] charging = Hex("03 50 00 0B 02 01 C9 21 07 01 90 65 04 A5 5A 00");
        var parsed = EweadnMouseCodec.ParseBasics(charging);

        Assert.NotNull(parsed);
        Assert.Equal(73, parsed!.Value.Percent);   // 0xC9 & 0x7F = 0x49 = 73
        Assert.True(parsed.Value.Charging);
    }

    [Fact]
    public void M6_非本协议响应返回null()
    {
        Assert.Null(EweadnMouseCodec.ParseBasics(Hex("03 51 00 0B 02 01 49 21")));
        Assert.Null(EweadnMouseCodec.ParseBasics(Hex("02 50 00 0B 02 01 49 21")));
        Assert.Null(EweadnMouseCodec.ParseBasics(new byte[] { 0x03 }));
    }

    [Fact]
    public void M7_电量越界返回null()
    {
        byte[] over = Hex("03 50 00 0B 02 01 7F 21");   // 0x7F = 127 > 100
        Assert.Null(EweadnMouseCodec.ParseBasics(over));
    }

    [Fact]
    public void M8_电量0为休眠空响应返回null()
    {
        // 13:50-13:55 实机连续采到：鼠标休眠时 dongle 回结构合法但电量字节为 0 的帧。
        // 真 0% 不存在（S9 电量耗尽自动关机），codec 层直接拒收，不落到读数过滤器刷 WARN。
        byte[] sleeping = Hex("03 50 00 0B 02 01 00 21 07 01 90 65 04 A5 5A 00");
        Assert.Null(EweadnMouseCodec.ParseBasics(sleeping));
    }
}
