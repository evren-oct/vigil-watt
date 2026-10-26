using VigilWatt.Core.Plugins.Mojhon;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： MojhonCodec —— 墨将 A5 帧协议的纯编解码。
/// 2026/10/26 00:45:00 workbuddy
/// 全部用例帧取自本机真机 USBPcap 实录（captures/mojhon-e2b7-20261026.pcap），
/// 35/35 帧校验通过，出处见 docs/hardware/mojhon.md §6.4。
/// </summary>
public class MojhonCodecTests
{
    /// <summary>把帧铺进 64 字节线上载荷（不足补零），模拟 USBPcap 抓到的形态。</summary>
    private static byte[] Wire(string frameHex)
    {
        var buf = new byte[MojhonCodec.FrameLength];
        var frame = Convert.FromHexString(frameHex);
        Array.Copy(frame, 0, buf, 0, frame.Length);
        return buf;
    }

    [Theory]
    [InlineData(0x04, "a50404ad")]   // 电量：A5+04+04 = 0xAD
    [InlineData(0x0B, "a5040bb4")]   // 实录 0x61（非电量）
    [InlineData(0xE2, "a504e28b")]   // 设备名："C4_SERVER"
    [InlineData(0xD6, "a504d67f")]   // 分块配置转储
    public void BuildRead_reproduces_captured_request(byte register, string expectedHex)
    {
        Assert.Equal(expectedHex, Convert.ToHexString(MojhonCodec.BuildRead(register)).ToLowerInvariant()[..8]);
    }

    [Fact]
    public void BuildRead_pads_to_wire_length()
    {
        var frame = MojhonCodec.BuildRead(MojhonCodec.RegisterBattery);
        Assert.Equal(MojhonCodec.FrameLength, frame.Length);
        Assert.All(frame[4..], b => Assert.Equal(0, b));
    }

    /// <summary>真机应答：官方助手显示 18% 时寄存器 0x04 回 `12 00`（未插充电线）。</summary>
    [Fact]
    public void Parses_captured_battery_reply_18()
    {
        var reply = Wire("a506041200c1");
        Assert.True(MojhonCodec.TryParse(reply, out byte register, out var payload));
        Assert.Equal(MojhonCodec.RegisterBattery, register);
        Assert.Equal(new byte[] { 0x12, 0x00 }, payload);
        Assert.True(MojhonCodec.TryParseBattery(payload, out int percent, out byte flag));
        Assert.Equal(18, percent);
        Assert.Equal(MojhonCodec.ChargingFlagDischarging, flag);
    }

    /// <summary>真机应答：官方软件关掉后独立探测两次，寄存器 0x04 回 `11 00` = 17%。</summary>
    [Fact]
    public void Parses_live_probe_reply_17()
    {
        var reply = Wire("a506041100c0");
        Assert.True(MojhonCodec.TryParse(reply, out byte register, out var payload));
        Assert.Equal(MojhonCodec.RegisterBattery, register);
        Assert.True(MojhonCodec.TryParseBattery(payload, out int percent, out byte flag));
        Assert.Equal(17, percent);
        Assert.Equal(MojhonCodec.ChargingFlagDischarging, flag);
    }

    /// <summary>
    /// 插上充电线后的真机应答：`a5 06 04 1f 01 cf`（31%，flag=0x01）、
    /// `a5 06 04 21 01 d1`（33%，flag=0x01）。与放电态的 0x00 构成 A/B 对照，
    /// 见 docs/hardware/mojhon.md §7.2。
    /// </summary>
    [Theory]
    [InlineData("a506041f01cf", 31)]
    [InlineData("a506042101d1", 33)]
    [InlineData("a506041c01cc", 28)]
    [InlineData("a506041601c6", 22)]
    public void Parses_charging_reply_flag_0x01(string frameHex, int expectedPercent)
    {
        var reply = Wire(frameHex);
        Assert.True(MojhonCodec.TryParse(reply, out byte register, out var payload));
        Assert.Equal(MojhonCodec.RegisterBattery, register);
        Assert.True(MojhonCodec.TryParseBattery(payload, out int percent, out byte flag));
        Assert.Equal(expectedPercent, percent);
        Assert.Equal(MojhonCodec.ChargingFlagCharging, flag);
    }

    /// <summary>未观测过的标志值不能被猜成"充电"或"放电"——必须原样透出，交给插件判 Unknown。</summary>
    [Theory]
    [InlineData(new byte[] { 0x20, 0x02 })]
    [InlineData(new byte[] { 0x20, 0xFF })]
    public void Unknown_charging_flag_is_passed_through(byte[] payload)
    {
        Assert.True(MojhonCodec.TryParseBattery(payload, out _, out byte flag));
        Assert.Equal(payload[1], flag);
    }

    /// <summary>只有 1 字节载荷（旧固件/瞬态）时百分比仍可取，标志报 Unknown 而不是默认成放电。</summary>
    [Fact]
    public void Short_payload_yields_unknown_flag()
    {
        Assert.True(MojhonCodec.TryParseBattery(new byte[] { 0x2A }, out int percent, out byte flag));
        Assert.Equal(42, percent);
        Assert.Equal(MojhonCodec.ChargingFlagUnknown, flag);
    }

    /// <summary>报文首字节是填充、帧从下标 1 起时也要能解析（两种回传形态都认）。</summary>
    [Fact]
    public void Tolerates_leading_pad_byte()
    {
        var reply = new byte[MojhonCodec.FrameLength + 1];
        Array.Copy(Wire("a506041100c0"), 0, reply, 1, MojhonCodec.FrameLength);
        Assert.True(MojhonCodec.TryParse(reply, out byte register, out var payload));
        Assert.Equal(MojhonCodec.RegisterBattery, register);
        Assert.True(MojhonCodec.TryParseBattery(payload, out int percent, out _));
        Assert.Equal(17, percent);
    }

    /// <summary>设备名应答带 ASCII 后缀，解析器不能因为载荷里有可打印字符就出错。</summary>
    [Fact]
    public void Parses_device_name_reply()
    {
        var reply = Wire("a510e258520043345f534552564552ee");
        Assert.True(MojhonCodec.TryParse(reply, out byte register, out var payload));
        Assert.Equal(MojhonCodec.RegisterDeviceName, register);
        Assert.Equal("C4_SERVER", System.Text.Encoding.ASCII.GetString(payload, 3, payload.Length - 3));
    }

    [Fact]
    public void Rejects_bad_checksum()
    {
        var reply = Wire("a506041200c2");   // 校验位改坏
        Assert.False(MojhonCodec.TryParse(reply, out _, out _));
    }

    [Fact]
    public void Rejects_unknown_header()
    {
        var reply = Wire("5a06041200c1");
        Assert.False(MojhonCodec.TryParse(reply, out _, out _));
    }

    [Fact]
    public void Rejects_truncated_and_zero_length()
    {
        Assert.False(MojhonCodec.TryParse(Array.Empty<byte>(), out _, out _));
        Assert.False(MojhonCodec.TryParse(new byte[] { 0xA5, 0x00, 0x04, 0x00 }, out _, out _));
        // 帧长 64 但缓冲只有 8 字节
        Assert.False(MojhonCodec.TryParse(Convert.FromHexString("a540040000000000"), out _, out _));
    }

    [Theory]
    [InlineData(new byte[] { 0x11, 0x00 }, 17)]
    [InlineData(new byte[] { 0x12, 0x00 }, 18)]
    [InlineData(new byte[] { 0x00 }, 0)]
    [InlineData(new byte[] { 0x64 }, 100)]
    public void Parses_percent_from_first_byte(byte[] payload, int expected)
    {
        Assert.True(MojhonCodec.TryParseBattery(payload, out int percent, out _));
        Assert.Equal(expected, percent);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x65 })]
    [InlineData(new byte[] { 0xFF })]
    public void Rejects_out_of_range_percent(byte[] payload)
    {
        Assert.False(MojhonCodec.TryParseBattery(payload, out _, out _));
    }

    [Fact]
    public void Checksum_is_sum_of_head_bytes()
    {
        Assert.Equal(0xAD, MojhonCodec.Checksum(new byte[] { 0xA5, 0x04, 0x04 }));
        Assert.Equal(0xB4, MojhonCodec.Checksum(new byte[] { 0xA5, 0x04, 0x0B }));
        // 跨 0xFF：0xA5+0x04+0xE2 = 0x18B，取低字节 0x8B
        Assert.Equal(0x8B, MojhonCodec.Checksum(new byte[] { 0xA5, 0x04, 0xE2 }));
    }

    /// <summary>
    /// 真机应答（USBPcap 实录）：读 0xE2 回 `A5 10 E2 | 58 52 00 "C4_SERVER" | EE`。
    /// 官方助手把 `C4_SERVER` 映射到 webName `GALE2`（exe 内 devices[] 目录表）。
    /// </summary>
    [Fact]
    public void Parses_device_mark_from_captured_reply()
    {
        var reply = Wire("a510e258520043345f534552564552ee");
        Assert.True(MojhonCodec.TryParse(reply, out byte register, out var payload));
        Assert.Equal(MojhonCodec.RegisterDeviceName, register);
        Assert.True(MojhonCodec.TryParseDeviceMark(payload, out string mark));
        Assert.Equal("C4_SERVER", mark);
    }

    /// <summary>接收器单独在位时的标识（官方目录里对应 Gale2_Dongle）。</summary>
    [Fact]
    public void Parses_dongle_mark()
    {
        var payload = new byte[] { 0x58, 0x52, 0x00, (byte)'C', (byte)'4', (byte)'_',
                                   (byte)'D', (byte)'O', (byte)'N', (byte)'G', (byte)'L', (byte)'E' };
        Assert.True(MojhonCodec.TryParseDeviceMark(payload, out string mark));
        Assert.Equal("C4_DONGLE", mark);
    }

    [Theory]
    [InlineData(new byte[0])]                       // 空载荷
    [InlineData(new byte[] { 0x58, 0x52 })]         // 没有分隔符
    [InlineData(new byte[] { 0x58, 0x52, 0x00 })]   // 分隔符后面没有内容
    [InlineData(new byte[] { 0x58, 0x52, 0x00, 0x01, 0x02 })]   // 非可打印字符
    public void Rejects_malformed_device_mark(byte[] payload)
    {
        Assert.False(MojhonCodec.TryParseDeviceMark(payload, out string mark));
        Assert.Equal("", mark);
    }
}
