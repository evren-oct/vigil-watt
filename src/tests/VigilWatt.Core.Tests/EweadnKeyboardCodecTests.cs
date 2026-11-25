using VigilWatt.Core.Plugins.Eweadn;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： EweadnKeyboardCodec —— 前行者 X87 键盘 2.4G 电量查询帧（33 字节 / 校验和在 [32]）。
/// 2026/10/18 13:40:00 Admin
/// 帧结构与实测依据见 docs/hardware/brands/eweadn.md §1.1。
/// </summary>
public class EweadnKeyboardCodecTests
{
    private static byte[] Hex(string s) => Convert.FromHexString(s.Replace(" ", ""));

    /// <summary>真机实测响应（100% 未充电）：首字节 0x00 是 report ID。</summary>
    private static readonly byte[] MeasuredResponse =
        Hex("00 20 01 00 64 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00");

    [Fact]
    public void K1_查询帧为33字节且标记与校验和正确()
    {
        byte[] frame = EweadnKeyboardCodec.BuildQuery();

        Assert.Equal(33, frame.Length);
        Assert.Equal((byte)0x00, frame[0]);                        // 该接口 report ID = 0
        Assert.Equal((byte)0x20, frame[1]);
        Assert.Equal((byte)0x01, frame[2]);
        Assert.Equal((byte)0x21, frame[32]);                       // 0x20 + 0x01 = 0x21
        // 校验和是「把校验位当 0 求和」，直接对成品再求和会把校验位自己算进去
        var zeroed = (byte[])frame.Clone();
        zeroed[32] = 0;
        Assert.Equal(EweadnKeyboardCodec.Checksum(zeroed), frame[32]);
    }

    [Fact]
    public void K2_校验和等于全帧求和取低8位()
    {
        var frame = new byte[33];
        frame[1] = 0xFF;
        frame[2] = 0x02;
        // 0xFF + 0x02 = 0x101 → 低 8 位 0x01
        Assert.Equal((byte)0x01, EweadnKeyboardCodec.Checksum(frame));
    }

    [Fact]
    public void K3_解析实测响应的电量()
    {
        Assert.Equal(100, EweadnKeyboardCodec.ParseBattery(MeasuredResponse));
    }

    [Fact]
    public void K4_兼容不带reportID前缀的布局()
    {
        // 去掉首字节 0x00 后标记仍应能定位（下标 1 分支）
        byte[] noId = Hex("20 01 00 64 00 00 00 00");
        Assert.Equal(100, EweadnKeyboardCodec.ParseBattery(noId));
    }

    [Fact]
    public void K5_电量为0视为无效()
    {
        byte[] zero = Hex("00 20 01 00 00 00 00 00");
        Assert.Null(EweadnKeyboardCodec.ParseBattery(zero));
    }

    [Fact]
    public void K6_电量超过100截为100()
    {
        byte[] over = Hex("00 20 01 00 FF 00 00 00");
        Assert.Equal(100, EweadnKeyboardCodec.ParseBattery(over));
    }

    [Fact]
    public void K7_非本协议帧返回null()
    {
        byte[] other = Hex("00 21 01 00 64 00 00 00");
        Assert.Null(EweadnKeyboardCodec.ParseBattery(other));
        Assert.Null(EweadnKeyboardCodec.ParseBattery(new byte[] { 0x00 }));
    }
}
