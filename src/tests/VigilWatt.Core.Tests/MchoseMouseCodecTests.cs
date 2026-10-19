using VigilWatt.Core.Plugins.Mchose;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： MchoseMouseCodec —— 迈从鼠标 0xFF01 协议（feature 0x11 / 事件 0x13）帧编解码。
/// 2026/10/19 19:50:00 Bamlax
/// 用例使用 A7 V2 Ultra+（3837:100B）真机原始帧；0xE2 在 docs/hardware/protocols.md §7。
/// </summary>
public class MchoseMouseCodecTests
{
    /// <summary>把解码后的 19 字节载荷还原成 hidapi/ReadInput 看到的原始帧（带 report ID、逐字节 XOR）。</summary>
    private static byte[] RawFromDecoded(byte[] decoded)
    {
        var raw = new byte[decoded.Length + 1];
        raw[0] = 0x13;
        for (int i = 0; i < decoded.Length; i++) raw[i + 1] = (byte)(decoded[i] ^ 0xFF);
        return raw;
    }

    [Fact]
    public void M1_查询命令与真机一致()
    {
        byte[] payload = MchoseMouseCodec.BuildStatusQueryPayload();
        Assert.Equal(20, payload.Length);
        Assert.Equal("F455" + new string('F', 36), Convert.ToHexString(payload));
    }

    [Fact]
    public void M2_真机E2帧解析出98和名字()
    {
        // 2026-10-19 实测解码载荷：E2 01 01 00 62 02 02 06 2C "A7 V2 Ultra"
        byte[] decoded = TestFrames.Hex("E2 01 01 00 62 02 02 06 2C 41 37 20 56 32 20 55 6C 74 72 61");
        Assert.True(MchoseMouseCodec.TryParseStatus(RawFromDecoded(decoded), out var status));
        Assert.Equal(98, status.Percent);
        Assert.Equal((byte)0x00, status.ChargeFlag);
        Assert.Equal(2, status.PollingIndex);
        Assert.Equal(1000, MchoseMouseCodec.PollingRate(status.PollingIndex));
        Assert.Equal("A7 V2 Ultra", status.Name);
    }

    [Fact]
    public void M3_兼容ReadInput在帧首多一个字节()
    {
        byte[] decoded = TestFrames.Hex("E2 01 01 00 64 02 02 06 2C 41 37 20 56 32 20 55 6C 74 72 61");
        byte[] raw = RawFromDecoded(decoded);
        byte[] prefixed = new byte[raw.Length + 1];
        Array.Copy(raw, 0, prefixed, 1, raw.Length);

        Assert.True(MchoseMouseCodec.TryParseStatus(prefixed, out var status));
        Assert.Equal(100, status.Percent);
    }

    [Fact]
    public void M4_E5握手帧与休眠畸形包都忽略()
    {
        byte[] e5 = RawFromDecoded(TestFrames.Hex("E5 0B 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"));
        Assert.False(MchoseMouseCodec.TryParseStatus(e5, out _));

        // 电量 0 / 回报率 0 / 名字为空 = 唤醒瞬间的畸形包
        byte[] garbled = RawFromDecoded(TestFrames.Hex("E2 01 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"));
        Assert.False(MchoseMouseCodec.TryParseStatus(garbled, out _));
    }

    [Fact]
    public void M5_电量越界拒绝()
    {
        byte[] bad = RawFromDecoded(TestFrames.Hex("E2 01 01 00 65 02 02 06 2C 41 37 20 56 32 20 55 6C 74 72 61"));
        Assert.False(MchoseMouseCodec.TryParseStatus(bad, out _));
    }
}
