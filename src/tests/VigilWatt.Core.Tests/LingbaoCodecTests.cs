using VigilWatt.Core.Plugins.Lingbao;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： LingbaoCodec —— 凌豹 M5PRO Max 的 FEA 特征帧（校验和与电量解析边界）。
/// 2026/10/24 17:48:00 L
/// 帧来源与本机实测记录见 docs/hardware/lingbao.md §1。
/// </summary>
public class LingbaoCodecTests
{
    /// <summary>本机 3151:402D 实测响应（SET 83 … 7C 后 GET）：[3]=0x53=83%。</summary>
    private static readonly byte[] RealReport =
        TestFrames.Hex("00 00 00 53 01 00 01 02 00 00 01 20 03 40 06 80 0c 00 19 90 65");

    [Fact]
    public void L1_查询帧_命令与校验和()
    {
        var payload = LingbaoCodec.BuildBatteryQuery();
        Assert.Equal(64, payload.Length);
        Assert.Equal(LingbaoCodec.CmdGetBattery, payload[0]);
        Assert.Equal(0x7C, payload[^1]);                       // 0xFF - 0x83
        Assert.Equal(LingbaoCodec.Checksum(payload), payload[^1]);
    }

    [Fact]
    public void L2_本机实测帧解析出83()
    {
        Assert.True(LingbaoCodec.TryParseBattery(RealReport, out int percent));
        Assert.Equal(83, percent);
    }

    [Fact]
    public void L3_越界与占位电量一律拒绝()
    {
        Assert.False(LingbaoCodec.TryParseBattery(Patch(3, 0x00), out _));   // 0% 休眠占位
        Assert.False(LingbaoCodec.TryParseBattery(Patch(3, 0x01), out _));   // 1% 占位
        Assert.False(LingbaoCodec.TryParseBattery(Patch(3, 0x65), out _));   // 101%
    }

    [Fact]
    public void L4_头字节不符即拒绝()
    {
        Assert.False(LingbaoCodec.TryParseBattery(Patch(0, 0x01), out _));   // report ID 不是 0
        Assert.False(LingbaoCodec.TryParseBattery(Patch(1, 0x07), out _));   // 接收器 ack 非法
        Assert.False(LingbaoCodec.TryParseBattery(Patch(2, 0x01), out _));   // 设备返回码非 0
    }

    [Fact]
    public void L5_短帧不抛异常()
    {
        Assert.False(LingbaoCodec.TryParseBattery(TestFrames.Hex("00 01"), out _));
        Assert.False(LingbaoCodec.TryParseBattery([], out _));
        Assert.Equal(0, LingbaoCodec.Checksum([]));
    }

    private static byte[] Patch(int index, byte value)
    {
        var copy = (byte[])RealReport.Clone();
        copy[index] = value;
        return copy;
    }
}
