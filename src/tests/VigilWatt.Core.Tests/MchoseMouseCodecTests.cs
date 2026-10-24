using VigilWatt.Core.Plugins.Mchose;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： MchoseMouseCodec —— 迈从鼠标 V2（E2 状态帧 / 触发帧 / 型号密码表）。
/// 2026/10/19 10:45:30 wuzih
/// 用例以 A7 V2 Pro 接收器（3837:100A）真机抓帧为基准（M10），型号表来自官方 getPassWord（M11/M12）。
/// </summary>
public class MchoseMouseCodecTests
{
    /// <summary>A7 V2 Pro 接收器真机帧：[0]=report ID 0x13 前缀，payload 取反后 [0]=0xE2、[3]=充电、[4]=电量。</summary>
    /// 原始 13 1D FE FE FF BB FD FD F9 D6 BE C8 DF A9 CD DF AF 8D 90 FF FF …
    /// 取反(去前缀) E2 01 01 00 44 … → 未充电 68%。
    private static readonly byte[] RealFrame =
        TestFrames.Hex("13 1D FE FE FF BB FD FD F9 D6 BE C8 DF A9 CD DF AF 8D 90 FF FF 00 00 00");

    [Fact]
    public void M10_真机E2帧解析_68pct_未充电()
    {
        var kind = MchoseMouseCodec.ParseStatus(RealFrame, out int pct, out bool chg);
        Assert.Equal(MouseV2PushKind.Status, kind);
        Assert.Equal(68, pct);
        Assert.False(chg);
    }

    [Fact]
    public void M11_充电位与电量位照常解析()
    {
        // [0]=report ID 前缀；payload 取反后 = E2 ? ? charge=1 level=99
        byte[] payload = { 0xE2, 0x01, 0x01, 0x01, 0x63, 0x00 };
        byte[] raw = new byte[payload.Length + 1];
        raw[0] = 0x13;
        for (int i = 0; i < payload.Length; i++) raw[i + 1] = (byte)(payload[i] ^ 0xFF);
        var kind = MchoseMouseCodec.ParseStatus(raw, out int pct, out bool chg);
        Assert.Equal(MouseV2PushKind.Status, kind);
        Assert.Equal(99, pct);
        Assert.True(chg);
    }

    [Fact]
    public void M12_电量越界_判StatusInvalid()
    {
        // charge=0, level=0（关机占位）
        byte[] raw = { 0x1D, 0xFF ^ 0xFF, 0x01 ^ 0xFF, 0x00 ^ 0xFF, 0xFF ^ 0xFF, 0x00 ^ 0xFF };
        var kind = MchoseMouseCodec.ParseStatus(raw, out _, out _);
        Assert.Equal(MouseV2PushKind.StatusInvalid, kind);
    }

    [Fact]
    public void M13_短帧与非E2帧_判NotStatus()
    {
        Assert.Equal(MouseV2PushKind.NotStatus, MchoseMouseCodec.ParseStatus(new byte[] { 0x13, 0xE2 ^ 0xFF }, out _, out _));
        // 非 E2 开头（取反后 = 0x41）→ NotStatus
        byte[] raw = { 0x13, 0xBE, 0xFE, 0xFE, 0xFE, 0xFE };
        Assert.Equal(MouseV2PushKind.NotStatus, MchoseMouseCodec.ParseStatus(raw, out _, out _));
    }

    [Fact]
    public void M14_型号密码表_样例命中与未收录()
    {
        Assert.Equal("G87", MchoseMouseCodec.ModelFromPassword(TestFrames.Hex("03 00 00 00 02 1C")));
        Assert.Equal("G87 V2", MchoseMouseCodec.ModelFromPassword(TestFrames.Hex("0A 00 00 00 00 34")));
        Assert.Null(MchoseMouseCodec.ModelFromPassword(TestFrames.Hex("01 02 03 04 05 06")));
        Assert.Null(MchoseMouseCodec.ModelFromPassword(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void M15_会话ACK提型号()
    {
        // 老真机 SessionAck 的密码段是 02 1C? 不是——用一段合成 ACK 验证布局即可
        byte[] ack = TestFrames.Hex("13 05 01 00 0A 03 03 00 00 00 02 1C 00 00 00 00 00 00 00 45");
        Assert.Equal("G87", MchoseMouseCodec.ModelFromSessionAck(ack));
        // reportId 前缀右移一字节后仍能识别
        byte[] shifted = new byte[ack.Length + 1];
        Array.Copy(ack, 0, shifted, 1, ack.Length);
        Assert.Equal("G87", MchoseMouseCodec.ModelFromSessionAck(shifted));
        // 4A 响应不是会话 ACK → null
        Assert.Null(MchoseMouseCodec.ModelFromSessionAck(TestFrames.Response66));
    }

    [Fact]
    public void M17_G75世代ACK_密码前置一个填充字节()
    {
        // 本机 G75 Pro 接收器（3554:FA09）真机会话 ACK：密码 03 00 00 00 01 15 从 [5] 开始
        byte[] ack = TestFrames.Hex("13 05 01 00 0A 03 00 00 00 01 15 01 00 00 00 00 00 00 00 3D");
        Assert.Equal("G75 Pro", MchoseMouseCodec.ModelFromSessionAck(ack));

        // AULA F99 Pro 同型 ACK：同一偏移但密码 03 00 00 00 01 58 不在迈从表里 → 不算迈从设备
        byte[] aula = TestFrames.Hex("13 05 01 00 0A 03 00 00 00 01 58 01 00 00 00 00 00 00 00 80");
        Assert.Null(MchoseMouseCodec.ModelFromSessionAck(aula));
    }

    [Fact]
    public void M16_触发帧_reportId原样_数据取反()
    {
        var frame = MchoseMouseCodec.BuildPokeFrame(65);
        Assert.Equal(65, frame.Length);
        Assert.Equal(0x11, frame[0]);          // Windows WriteOutput: buffer[0] = report ID，不取反
        Assert.Equal(0x06 ^ 0xFF, frame[1]);   // 数据段取反（官方 WebHID data = 06 00×19 ^ 0xFF）
        Assert.Equal(0xFF, frame[2]);
        Assert.Equal(0xFF, frame[64]);
    }
}
