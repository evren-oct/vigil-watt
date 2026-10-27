using VigilWatt.Core.Plugins.Asus;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： AsusHeadsetCodec —— 华硕 ROG 耳机 0xCC 帧的构造、回显校验与 POWER/CHARGING 解析。
/// 2026/10/24 01:30:00 Leejangho
/// 样本帧取自本机 ROG DELTA II（0B05:1AFA），见 docs/hardware/asus-rog.md §2。
/// </summary>
public class AsusHeadsetCodecTests
{
    /// <summary>真机 POWER 应答：睡眠 5 分、电量 0x43=67%、低电阈值 20、语音开。</summary>
    private static readonly byte[] PowerReply =
        Convert.FromHexString("CC1207000005431401" + new string('0', (64 - 9) * 2));

    /// <summary>真机 CHARGING 应答：data[0]=1 表示充电中。</summary>
    private static readonly byte[] ChargingReply =
        Convert.FromHexString("CC1208000001000000" + new string('0', (64 - 9) * 2));

    [Fact]
    public void Power_request_starts_with_report_group_command()
    {
        var frame = AsusHeadsetCodec.BuildRead(AsusHeadsetCodec.CmdPower);
        Assert.Equal(AsusHeadsetCodec.ReportLength, frame.Length);
        Assert.Equal("CC1207000000", Convert.ToHexString(frame[..6]));
    }

    [Fact]
    public void Parse_accepts_reply_echoing_command()
    {
        Assert.True(AsusHeadsetCodec.TryParse(PowerReply, AsusHeadsetCodec.CmdPower, out var data));
        Assert.Equal(64 - 5, data.Length);
    }

    [Fact]
    public void Parse_rejects_reply_to_other_command()
    {
        // 迟到的上一条命令应答不能被当成本轮结果，否则电量会"卡住一个旧值"。
        Assert.False(AsusHeadsetCodec.TryParse(PowerReply, AsusHeadsetCodec.CmdCharging, out _));
    }

    [Fact]
    public void Parse_rejects_foreign_report_id()
    {
        var broken = (byte[])PowerReply.Clone();
        broken[0] = 0x5A;
        Assert.False(AsusHeadsetCodec.TryParse(broken, AsusHeadsetCodec.CmdPower, out _));
    }

    [Fact]
    public void Power_reply_yields_percent_and_low_battery_settings()
    {
        Assert.True(AsusHeadsetCodec.TryParse(PowerReply, AsusHeadsetCodec.CmdPower, out var data));
        Assert.True(AsusHeadsetCodec.TryParsePower(data, out int percent, out int lowLevel, out bool lowVoice));
        Assert.Equal(67, percent);
        Assert.Equal(20, lowLevel);
        Assert.True(lowVoice);
    }

    [Fact]
    public void Power_rejects_not_ready_placeholder()
    {
        // 0xFE 是"电量未就绪"占位值，当成百分比会显示成荒谬的数字。
        var data = new byte[] { 5, 0xFE, 20, 1 };
        Assert.False(AsusHeadsetCodec.TryParsePower(data, out _, out _, out _));
    }

    [Fact]
    public void Power_rejects_out_of_range_percent()
    {
        var data = new byte[] { 5, 200, 20, 1 };
        Assert.False(AsusHeadsetCodec.TryParsePower(data, out _, out _, out _));
    }

    [Fact]
    public void Charging_reply_reports_charging()
    {
        Assert.True(AsusHeadsetCodec.TryParse(ChargingReply, AsusHeadsetCodec.CmdCharging, out var data));
        Assert.True(AsusHeadsetCodec.TryParseCharging(data, out bool charging));
        Assert.True(charging);
    }

    [Fact]
    public void Charging_reply_reports_discharging_when_zero()
    {
        var data = new byte[] { 0 };
        Assert.True(AsusHeadsetCodec.TryParseCharging(data, out bool charging));
        Assert.False(charging);
    }

    [Fact]
    public void Short_reply_is_rejected()
    {
        Assert.False(AsusHeadsetCodec.TryParse(new byte[] { 0xCC, 0x12 }, AsusHeadsetCodec.CmdPower, out _));
    }
}
