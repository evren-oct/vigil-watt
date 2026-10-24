using VigilWatt.Core.Plugins.HyperX;
using VigilWatt.Core.Models;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： Cf2Codec —— HyperX CF2 状态帧（FB 09 充电事件 / FB 13 电量 / FB 0A 在线）解析。
/// 2026/10/20 22:28:00 Yang
/// 帧结构与充电阶段字段的证伪记录见 docs/hardware/protocols.md §6。
/// </summary>
public class Cf2CodecTests
{
    private static byte[] Hex(string s) => Convert.FromHexString(s.Replace(" ", ""));

    [Fact]
    public void B1_心跳与官方软件往返帧识别为无信息()
    {
        Assert.True(Cf2Codec.IsHeartbeat(Hex("45 02 00 00")));
        // 官方软件的通信往返帧：FF 01 头 + 命令字落在下标 14（用代码构造，避免手数偏移量出错）
        var roundTrip = new byte[18];
        roundTrip[0] = 0xFF;
        roundTrip[1] = 0x01;
        roundTrip[14] = 0x44;
        roundTrip[15] = 0x01;
        Assert.True(Cf2Codec.IsHeartbeat(roundTrip));
        Assert.False(Cf2Codec.IsHeartbeat(Hex("FB 13 00 5E 00")));
    }

    [Fact]
    public void B2_插线事件判充电中且不携带电量()
    {
        var p = Cf2Codec.Parse(Hex("FB 09 01 00 00"));
        Assert.NotNull(p);
        Assert.Null(p!.Value.Percent);
        Assert.Equal(ChargingState.Charging, p.Value.Charging);
        Assert.False(p.Value.CarriesLevel);
    }

    [Fact]
    public void B3_拔线事件判放电()
    {
        Assert.Equal(ChargingState.Discharging, Cf2Codec.Parse(Hex("FB 09 00 00"))!.Value.Charging);
    }

    [Fact]
    public void B4_电量帧只带百分比不带充电状态()
    {
        // 2026-10-19 实测：充电 94% 的帧 [4] 是充电阶段编号，不是充电标志
        var p = Cf2Codec.Parse(Hex("FB 13 00 5E 00 00 00"));
        Assert.NotNull(p);
        Assert.Equal(94, p!.Value.Percent);
        Assert.True(p.Value.CarriesLevel);
        Assert.Equal(ChargingState.Unknown, p.Value.Charging);   // 必须沿用设备当前状态
    }

    [Fact]
    public void B5_离线与在线事件()
    {
        Assert.True(Cf2Codec.Parse(Hex("FB 0A 00 00"))!.Value.Offline);
        var online = Cf2Codec.Parse(Hex("FB 0A 01 00"))!;
        Assert.False(online.Value.Offline);
        Assert.Null(online.Value.Percent);
    }

    [Fact]
    public void B6_开机重连帧解析电量()
    {
        Assert.Equal(44, Cf2Codec.ParsePowerOnLevel(Hex("51 02 2C 00 1A 00 C0 0E 01 FF 0A")));
        Assert.Null(Cf2Codec.ParsePowerOnLevel(Hex("51 03 2C 00")));
        Assert.Null(Cf2Codec.ParsePowerOnLevel(null));
    }

    [Fact]
    public void B7_灯效与设备信息帧不采信()
    {
        Assert.Null(Cf2Codec.Parse(Hex("55 02 B0 04 00")));
        Assert.Null(Cf2Codec.Parse(Hex("61 02 49 01 00")));
        Assert.Null(Cf2Codec.Parse(Hex("FB 0B 01 00")));
    }

    [Fact]
    public void B8_越界电量不采信()
    {
        Assert.Null(Cf2Codec.Parse(Hex("FB 13 00 FF 00")));
    }

    [Fact]
    public void B9_过短帧不抛异常()
    {
        // 帧长不足时必须安全返回 null：电量字节在下标 3，短帧取它会越界
        Assert.Null(Cf2Codec.Parse(Hex("FB 13 00")));
        Assert.Null(Cf2Codec.Parse(Hex("FB 09 01")));
        Assert.Null(Cf2Codec.Parse(Hex("FB 0A 00")));
    }
}