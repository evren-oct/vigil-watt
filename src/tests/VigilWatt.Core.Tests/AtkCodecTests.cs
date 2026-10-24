using VigilWatt.Core.Plugins.Atk;
using VigilWatt.Core.Models;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： AtkCodec —— ATK 鼠标 17 字节电量查询协议（report 0x08 / cmd 0x04）帧编解码。
/// 2026/10/20 22:26:00 Yang
/// 帧结构与实测依据见 docs/hardware/atk.md。
/// </summary>
public class AtkCodecTests
{
    private static byte[] Hex(string s) => Convert.FromHexString(s.Replace(" ", ""));

    /// <summary>本机实测放电帧（2026-10-18）：95% + 电压 4092mV。</summary>
    private static readonly byte[] DischargingFrame =
        Hex("08 04 00 00 00 02 5F 00 0F FC 00 00 00 00 00 00 DD");

    /// <summary>按协议构造的边界样本：命令回显合法但电量字节为 0。
    /// 注：本机实测休眠时固件**完全不回应**（不产生任何帧），故此样本仅用于锁住
    /// "不校验电量下界就会读出 0%"这一防护，不代表抓包证据。</summary>
    private static readonly byte[] SleepFrame =
        Hex("08 04 01 00 00 00 00 00 00 00 00 00 00 00 00 00 48");

    /// <summary>鼠标普通移动报文——不能被当成电量帧（缺 expect 校验时的经典假读数来源）。</summary>
    private static readonly byte[] MouseMotionFrame =
        Hex("08 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F 10");

    /// <summary>按协议重算 17 字节帧的校验和（op 帧口径：0x4D − 前 15 字节和）。</summary>
    private static byte[] FixChecksum(byte[] frame)
    {
        int sum = 0;
        for (int i = 1; i < 16; i++) sum += frame[i];
        frame[16] = (byte)((0x4D - (sum & 0xFF)) & 0xFF);
        return frame;
    }

    [Fact]
    public void A1_构造查询帧与协议一致()
    {
        byte[] q = AtkCodec.BuildQuery();
        Assert.Equal(17, q.Length);
        Assert.Equal(AtkCodec.ReportId, q[0]);
        Assert.Equal(AtkCodec.BatteryCmd, q[1]);
        // 校验 = (0x4D − op 帧前 15 字节之和) & 0xFF = 0x49
        Assert.Equal("0804" + new string('0', 28) + "49", Convert.ToHexString(q));
    }

    [Fact]
    public void A2_解析实测放电帧得95()
    {
        var r = AtkCodec.TryParseReply(DischargingFrame);
        Assert.NotNull(r);
        Assert.Equal(95, r!.Value.Percent);
        Assert.Equal(ChargingState.Discharging, r.Value.Charging);
    }

    [Fact]
    public void A3_休眠占位帧判为无效()
    {
        // 这是参考实现漏掉的那一条：命令回显合法，不校验电量下界就会读出 0%
        Assert.Null(AtkCodec.TryParseReply(SleepFrame));
    }

    [Fact]
    public void A4_非协议报文不采信()
    {
        Assert.Null(AtkCodec.TryParseReply(MouseMotionFrame));
        Assert.Null(AtkCodec.TryParseReply(null));
        Assert.Null(AtkCodec.TryParseReply(Hex("08 04")));
    }

    [Fact]
    public void A5_充电标志非零判充电中()
    {
        byte[] f = FixChecksum((byte[])DischargingFrame.Clone());
        f[7] = 0x01;
        FixChecksum(f);
        Assert.Equal(ChargingState.Charging, AtkCodec.TryParseReply(f)!.Value.Charging);
    }

    [Fact]
    public void A6_百分之一律拒绝()
    {
        byte[] f = FixChecksum((byte[])DischargingFrame.Clone());
        f[6] = 0x01;
        FixChecksum(f);
        Assert.Null(AtkCodec.TryParseReply(f));
    }

    [Fact]
    public void A7_电压大端解析()
    {
        Assert.Equal(4092, AtkCodec.ReadMillivolts(DischargingFrame));
    }

    [Fact]
    public void A8_容忍前置占位字节的读路径()
    {
        // 某些读路径会在 report ID 前多带一个 0
        var prefixed = new byte[DischargingFrame.Length + 1];
        Array.Copy(DischargingFrame, 0, prefixed, 1, DischargingFrame.Length);
        Assert.Equal(95, AtkCodec.TryParseReply(prefixed)!.Value.Percent);
    }

    // ---------------------------------------------------------------- 校验和

    [Fact]
    public void B1_校验和错误一律拒绝()
    {
        // cmd 与电量位都合法，只有校验和不对——不校验和就会把它读成 95%
        byte[] f = (byte[])DischargingFrame.Clone();
        f[16] = 0x00;
        Assert.Null(AtkCodec.TryParseReply(f));
        Assert.Null(AtkCodec.ReadMillivolts(f));
    }

    [Fact]
    public void B2_按协议算对的校验和才采信()
    {
        // 反向自证：把校验和按协议算对后，同一帧必须能被解析
        byte[] f = FixChecksum((byte[])DischargingFrame.Clone());
        f[16] = 0x00;
        FixChecksum(f);
        Assert.Equal(95, AtkCodec.TryParseReply(f)!.Value.Percent);
    }

    [Fact]
    public void B3_设备回显本机查询帧不计为应答()
    {
        // 固件把请求原样回显：校验和是对的、电量位是 0 —— 必须靠电量下界挡住，
        // 否则会把"回显"读成 0% 的假读数。
        Assert.Null(AtkCodec.TryParseReply(AtkCodec.BuildQuery()));
    }

    [Fact]
    public void B4_报ID被剥掉的16字节op帧同样可解析()
    {
        // 线上 17 字节 = report ID + op 帧；op 帧自身 16 字节，校验口径与 report ID 无关
        var op = DischargingFrame.Skip(1).ToArray();
        Assert.Equal(16, op.Length);
        Assert.Equal(95, AtkCodec.TryParseReply(op)!.Value.Percent);
    }

    [Fact]
    public void B5_两种帧长口径的校验和互相等价()
    {
        // 含 report ID 的 17 字节：chk = (0x55 − 前 16 字节和) & 0xFF
        // op 帧 16 字节：      chk = (0x4D − 前 15 字节和) & 0xFF
        // 两式等价（0x4D = 0x55 − 0x08），故两种线上形态用同一个 codec 即可
        int withId = 0;
        for (int i = 0; i < 16; i++) withId += DischargingFrame[i];
        withId &= 0xFF;
        int opOnly = 0;
        for (int i = 1; i < 16; i++) opOnly += DischargingFrame[i];
        opOnly &= 0xFF;

        Assert.Equal(DischargingFrame[16], (byte)((0x55 - withId) & 0xFF));
        Assert.Equal(DischargingFrame[16], (byte)((0x4D - opOnly) & 0xFF));
    }

    // ---------------------------------------------------------------- 长度判据

    [Fact]
    public void C1_严格接口判据要求17进17出()
    {
        Assert.True(AtkCodec.IsSupportedInterface(17, 17));
        Assert.False(AtkCodec.IsSupportedInterface(16, 17));
        Assert.False(AtkCodec.IsSupportedInterface(17, 16));
        Assert.False(AtkCodec.IsSupportedInterface(49, 49));
        Assert.False(AtkCodec.IsSupportedInterface(0, 0));
    }
}
