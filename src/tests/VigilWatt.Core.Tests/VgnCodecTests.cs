using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Vgn;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： VgnCodec —— VGN 蜻蜓（kc2 协议）的帧构造、应答解析与设备匹配。
/// 2026/10/19 新增
///
/// 用例 V2/V3 用的是**真机原始帧**（391D:1026，有线充电中，2026-10-19 抓于本机）：
/// <code>
///   B4 06 00 20 20 60 90 01 20 03 40 06 80 0C 00 19 01 06 00 05 A4 50 C3 00 00 00 00 BB ...
/// </code>
/// 电量在 payload[19]（= 缓冲下标 20），真机值 0xA4 → 低 7 位 0x24 = 36%，bit7 = 1 表示充电中。
/// </summary>
public class VgnCodecTests
{
    /// <summary>真机应答（含 report ID 首字节）。</summary>
    private static byte[] RealConfigFrame()
    {
        var frame = new byte[VgnCodec.FrameLength];
        frame[0] = 0xB4;   // 应答 report ID
        frame[1] = 0x06;   // 命令回显
        frame[2] = 0x00;
        frame[3] = 0x20;
        frame[4] = 0x20;
        frame[5] = 0x60;
        frame[6] = 0x90;
        frame[7] = 0x01;
        frame[8] = 0x20;
        frame[9] = 0x03;
        frame[10] = 0x40;
        frame[11] = 0x06;
        frame[12] = 0x80;
        frame[13] = 0x0C;
        frame[14] = 0x00;
        frame[15] = 0x19;
        frame[16] = 0x01;
        frame[17] = 0x06;
        frame[18] = 0x00;
        frame[19] = 0x05;
        frame[20] = 0xA4;  // payload[19]：低 7 位=36%，bit7=充电
        frame[21] = 0x50;
        frame[22] = 0xC3;
        frame[28] = 0xBB;
        return frame;
    }

    [Fact]
    public void V1_请求帧是64字节且首字节为reportId()
    {
        var frame = VgnCodec.BuildConfigRequest();

        Assert.Equal(64, frame.Length);
        Assert.Equal(0xB3, frame[0]);   // 官方驱动 kcMs2 的 reportId
        Assert.Equal(0x06, frame[1]);   // 配置查询
        Assert.Equal("B306000000000000", Convert.ToHexString(frame)[..16]);
    }

    [Fact]
    public void V2_真机帧解析出36percent充电中()
    {
        Assert.True(VgnCodec.TryParseBattery(RealConfigFrame(), out int percent, out var charging));
        Assert.Equal(36, percent);
        Assert.Equal(ChargingState.Charging, charging);
    }

    [Fact]
    public void V3_不带reportId的纯数据层也能解析()
    {
        var withId = RealConfigFrame();
        var dataOnly = new byte[VgnCodec.FrameLength - 1];
        Array.Copy(withId, 1, dataOnly, 0, dataOnly.Length);

        Assert.True(VgnCodec.TryParseBattery(dataOnly, out int percent, out var charging));
        Assert.Equal(36, percent);
        Assert.Equal(ChargingState.Charging, charging);
    }

    [Fact]
    public void V4_bit7清零即放电()
    {
        // 真机读数 0xA4 去掉充电位 → 0x24 = 36%，应判为放电
        var frame = RealConfigFrame();
        frame[20] = 0x24;

        Assert.True(VgnCodec.TryParseBattery(frame, out int percent, out var charging));
        Assert.Equal(36, percent);
        Assert.Equal(ChargingState.Discharging, charging);
    }

    [Fact]
    public void V5_电量只取低7位()
    {
        var frame = RealConfigFrame();
        frame[20] = (byte)(100 | 0x80);   // 100% + 充电位

        Assert.True(VgnCodec.TryParseBattery(frame, out int percent, out var charging));
        Assert.Equal(100, percent);
        Assert.Equal(ChargingState.Full, charging);
    }

    [Fact]
    public void V6_高7位越界一律拒绝()
    {
        var frame = RealConfigFrame();
        frame[20] = 0x7F;   // 127%，低 7 位仍是 127 → 不可能是百分比
        Assert.False(VgnCodec.TryParseBattery(frame, out _, out _));
    }

    [Fact]
    public void V7_错误回显帧不被当成电量()
    {
        // 真机里不受支持的命令回的是 B6 E4 07 <cmd> …；这里命令位是 E4，不是 0x06
        var frame = RealConfigFrame();
        frame[1] = 0xE4;
        frame[2] = 0x07;
        frame[3] = 0x06;

        Assert.False(VgnCodec.TryParseBattery(frame, out _, out _));
    }

    [Fact]
    public void V8_全零空缓冲不被当成0percent()
    {
        // 读超时/驱动返回空缓冲时，若只看 payload[19] 会读出 0%（"电量耗尽"），必须靠结构校验挡掉
        var frame = new byte[VgnCodec.FrameLength];
        frame[1] = 0x06;

        Assert.False(VgnCodec.TryParseBattery(frame, out _, out _));
    }

    [Fact]
    public void V9_命令不符的帧不被当成电量()
    {
        var frame = RealConfigFrame();
        frame[1] = 0x04;   // 版本应答（真机 04 05 "1.0.0"）

        Assert.False(VgnCodec.TryParseBattery(frame, out _, out _));
    }

    [Fact]
    public void V10_短帧不越界()
    {
        Assert.False(VgnCodec.TryParseBattery(new byte[5], out _, out _));
        Assert.False(VgnCodec.TryParseBattery(Array.Empty<byte>(), out _, out _));
        Assert.False(VgnCodec.TryParseBattery(null!, out _, out _));
    }

    [Fact]
    public void V11_连接状态帧能读出在线()
    {
        var frame = new byte[VgnCodec.FrameLength];
        frame[0] = 0xB4;
        frame[1] = VgnCodec.CmdConnectionStatus;
        frame[2] = 0x00;
        frame[3] = 0x01;   // payload[2] == 1 → 在线

        Assert.True(VgnCodec.TryParseOnline(frame, out bool online));
        Assert.True(online);

        frame[3] = 0x00;
        Assert.True(VgnCodec.TryParseOnline(frame, out bool asleep));
        Assert.False(asleep);
    }

    // ── 匹配规则 ──────────────────────────────────────────────────────

    [Fact]
    public void R1_无线dongle被认领()
    {
        Assert.True(VgnCodec.IsMatch(0x391D, 0x1A23, 0xFFC1, 0x0001, 64, 64));
    }

    [Fact]
    public void R2_有线模式换个PID也要认()
    {
        // 插线后 PID 变 0x1026，不认它充电期间设备会整个消失
        Assert.True(VgnCodec.IsMatch(0x391D, 0x1026, 0xFFC1, 0x0001, 64, 64));
    }

    [Fact]
    public void R3_33字节的0x008C接口不抢()
    {
        Assert.False(VgnCodec.IsMatch(0x391D, 0x1A23, 0x008C, 0x0001, 33, 33));
    }

    [Fact]
    public void R4_别的VID或PID一律不认()
    {
        Assert.False(VgnCodec.IsMatch(0x36B0, 0x3002, 0xFFC1, 0x0001, 64, 64));   // PMO 键盘
        Assert.False(VgnCodec.IsMatch(0x391D, 0x1A24, 0xFFC1, 0x0001, 64, 64));   // 未登记的 PID
        Assert.False(VgnCodec.IsMatch(0x5253, 0x1021, 0xFFC1, 0x0001, 64, 64));   // 迈从鼠标
    }

    [Fact]
    public void R5_usage不对不认()
    {
        Assert.False(VgnCodec.IsMatch(0x391D, 0x1A23, 0xFF00, 0x0001, 64, 64));
        Assert.False(VgnCodec.IsMatch(0x391D, 0x1A23, 0xFFC1, 0x0002, 64, 64));
    }
}
