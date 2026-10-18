using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Mchose;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： AULA F99 Pro 0x13 会话/推送帧的回归用例（复用 MchoseV1Codec）。
/// 2026/10/19 02:45:00 sqcxz
/// 真机：13 05 01 … 80（会话 ACK）、13 0A 01 00 04 05 64 01 … 8C（100% 推送）。
/// </summary>
public class AulaProtocolTests
{
    private static readonly byte[] SessionAck =
        Convert.FromHexString("130501000a030000000158010000000000000080");

    private static readonly byte[] BatteryPush100 =
        Convert.FromHexString("130a01000405640100000000000000000000008c");

    [Fact]
    public void A1_真机会话ACK不是电量帧()
    {
        Assert.Equal(V1PushKind.NotPush, MchoseV1Codec.ParsePush(SessionAck).Kind);
        Assert.Equal(V1ResponseKind.NotResponse,
            MchoseV1Codec.ParseBatteryResponse(SessionAck, out _, out _));
    }

    [Fact]
    public void A2_真机推送解析出100和放电标志()
    {
        var push = MchoseV1Codec.ParsePush(BatteryPush100);
        Assert.Equal(V1PushKind.Battery, push.Kind);
        Assert.Equal(100, push.Percent);
        Assert.Equal(0x01, push.ChargeFlag);
        Assert.Equal(ChargingState.Full,
            ChargingStateMap.FromChargingFlag((push.ChargeFlag & 0x10) != 0, push.Percent));
    }

    [Fact]
    public void A3_90percent推送解析正确()
    {
        var frame = Convert.FromHexString("130a010004055a01000000000000000000000082");
        var push = MchoseV1Codec.ParsePush(frame);
        Assert.Equal(V1PushKind.Battery, push.Kind);
        Assert.Equal(90, push.Percent);
        Assert.Equal(ChargingState.Discharging,
            ChargingStateMap.FromChargingFlag((push.ChargeFlag & 0x10) != 0, push.Percent));
    }

    [Fact]
    public void A4_4A响应解析出90()
    {
        var frame = Convert.FromHexString("134a0100025a01000000000000000000000000bb");
        Assert.Equal(V1ResponseKind.Battery,
            MchoseV1Codec.ParseBatteryResponse(frame, out int percent, out byte flag));
        Assert.Equal(90, percent);
        Assert.Equal(0x01, flag);
    }

    [Fact]
    public void A5_10标志位表示充电中()
    {
        Assert.Equal(ChargingState.Charging, ChargingStateMap.FromChargingFlag(true, 90));
    }

    [Fact]
    public void A6_真机0x11标志表示充电中()
    {
        var frame = Convert.FromHexString("130a01000405641100000000000000000000009c");
        var push = MchoseV1Codec.ParsePush(frame);
        Assert.Equal(V1PushKind.Battery, push.Kind);
        Assert.Equal(100, push.Percent);
        Assert.Equal(0x11, push.ChargeFlag);
        Assert.Equal(ChargingState.Charging,
            ChargingStateMap.FromChargingFlag((push.ChargeFlag & 0x10) != 0, push.Percent));
    }

    [Fact]
    public void A7_真机0x10标志表示充电中()
    {
        var frame = Convert.FromHexString("130a01000405641000000000000000000000009b");
        var push = MchoseV1Codec.ParsePush(frame);
        Assert.Equal(V1PushKind.Battery, push.Kind);
        Assert.Equal(100, push.Percent);
        Assert.Equal(0x10, push.ChargeFlag);
        Assert.Equal(ChargingState.Charging,
            ChargingStateMap.FromChargingFlag((push.ChargeFlag & 0x10) != 0, push.Percent));
    }
}
