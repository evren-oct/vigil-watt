using VigilWatt.Core.Plugins.Atk;
using VigilWatt.Core.Models;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： AtkCodec —— ATK 鼠标 17 字节电量查询协议（report 0x08 / cmd 0x04）帧编解码。
/// 2026/10/20 22:26:00 Yang
/// 帧结构与实测依据见 docs/hardware/protocols.md §5。
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

    [Fact]
    public void A1_构造查询帧与协议一致()
    {
        byte[] q = AtkCodec.BuildQuery();
        Assert.Equal(17, q.Length);
        Assert.Equal(AtkCodec.ReportId, q[0]);
        Assert.Equal(AtkCodec.BatteryCmd, q[1]);
        // 校验 = (0x55 − 前 15 字节之和) & 0xFF = 0x49
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
        byte[] f = (byte[])DischargingFrame.Clone();
        f[7] = 0x01;
        Assert.Equal(ChargingState.Charging, AtkCodec.TryParseReply(f)!.Value.Charging);
    }

    [Fact]
    public void A6_百分之一律拒绝()
    {
        byte[] f = (byte[])DischargingFrame.Clone();
        f[6] = 0x01;
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
        // 某些读路径会在 report ID 前多带一个 0；report ID 本身被剥掉时无法解析，不在兼容范围内
        var prefixed = new byte[DischargingFrame.Length + 1];
        Array.Copy(DischargingFrame, 0, prefixed, 1, DischargingFrame.Length);
        Assert.Equal(95, AtkCodec.TryParseReply(prefixed)!.Value.Percent);
    }
}