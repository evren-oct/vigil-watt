using VigilWatt.Core.Plugins.Lingbao;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： LingbaoCodec —— 凌豹 M5PRO Max 的状态帧（命令字节、电量与充电位）。
/// 2026/10/25 14:25:00 L
/// 帧来自官方驱动抓包与本机插拔充电线实测，见 docs/hardware/brands/lingbao.md §1。
/// </summary>
public class LingbaoCodecTests
{
    /// <summary>拔线（2.4G，放电）：[3]=0x5f=95、[5]=0x00。</summary>
    private static readonly byte[] Discharging =
        TestFrames.Hex("00 00 00 5f 01 00 01 02 00 00 00 00 00 00 00 00");

    /// <summary>插充电线：[3]=0x5a=90、[5]=0x01。</summary>
    private static readonly byte[] Charging =
        TestFrames.Hex("00 00 00 5a 01 01 01 02 00 00 00 00 00 00 00 00");

    [Fact]
    public void L1_查询帧_只有命令字节()
    {
        var payload = LingbaoCodec.BuildStatusQuery();
        Assert.Equal(64, payload.Length);
        Assert.Equal(0xF7, payload[0]);
        // 官方驱动发的是 00 f7 00…00：没有校验和，后面全 0
        Assert.All(payload[1..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void L2_放电帧_95且未充电()
    {
        Assert.True(LingbaoCodec.TryParseStatus(Discharging, out int percent, out bool charging));
        Assert.Equal(95, percent);
        Assert.False(charging);
    }

    [Fact]
    public void L3_插线帧_90且充电中()
    {
        Assert.True(LingbaoCodec.TryParseStatus(Charging, out int percent, out bool charging));
        Assert.Equal(90, percent);
        Assert.True(charging);
    }

    [Fact]
    public void L4_越界与占位电量一律拒绝()
    {
        Assert.False(LingbaoCodec.TryParseStatus(Patch(3, 0x00), out _, out _));   // 0% 休眠占位
        Assert.False(LingbaoCodec.TryParseStatus(Patch(3, 0x01), out _, out _));   // 1% 占位
        Assert.False(LingbaoCodec.TryParseStatus(Patch(3, 0x65), out _, out _));   // 101%
    }

    [Fact]
    public void L5_头字节不符即拒绝()
    {
        Assert.False(LingbaoCodec.TryParseStatus(Patch(0, 0x01), out _, out _));   // report ID 不是 0
        Assert.False(LingbaoCodec.TryParseStatus(Patch(1, 0x07), out _, out _));   // 接收器 ack 非法
        Assert.False(LingbaoCodec.TryParseStatus(Patch(2, 0x01), out _, out _));   // 设备返回码非 0
    }

    [Fact]
    public void L6_短帧不抛异常()
    {
        Assert.False(LingbaoCodec.TryParseStatus(TestFrames.Hex("00 01"), out _, out _));
        Assert.False(LingbaoCodec.TryParseStatus([], out _, out _));
    }

    private static byte[] Patch(int index, byte value)
    {
        var copy = (byte[])Discharging.Clone();
        copy[index] = value;
        return copy;
    }
}
