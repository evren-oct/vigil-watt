using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Compx;
using VigilWatt.Core.Plugins.Mchose;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 凌豹 K98 0x13 会话/推送帧回归，以及 3554:FA09 公版接收器的型号码分流。
/// 2026/10/20 17:20:00 Bamlax
/// 真机样本见 docs/hardware/protocols.md §12；AULA 样本见 §9。
/// </summary>
public class LingbaoProtocolTests
{
    private static readonly byte[] K98SessionAck =
        Convert.FromHexString("130501000a03000000021201000000000000003b");

    private static readonly byte[] AulaSessionAck =
        Convert.FromHexString("130501000a030000000158010000000000000080");

    private static readonly byte[] K98BatteryPush100 =
        Convert.FromHexString("130a01000405640100000000000000000000008c");

    [Fact]
    public void L1_K98会话ACK型号码是0212()
    {
        Assert.True(CompxSessionAck.TryGetModelCode(K98SessionAck, out ushort model));
        Assert.Equal(LingbaoPluginModelCode, model);
    }

    [Fact]
    public void L2_AULA会话ACK型号码是0158_不会被凌豹认领()
    {
        Assert.True(CompxSessionAck.TryGetModelCode(AulaSessionAck, out ushort model));
        Assert.Equal((ushort)0x0158, model);
        Assert.NotEqual(LingbaoPluginModelCode, model);
    }

    [Fact]
    public void L3_非会话ACK不产生型号码()
    {
        Assert.False(CompxSessionAck.TryGetModelCode(K98BatteryPush100, out _));
    }

    [Fact]
    public void L4_兼容ReadInput带reportId前缀()
    {
        byte[] shifted = new byte[K98SessionAck.Length + 1];
        Array.Copy(K98SessionAck, 0, shifted, 1, K98SessionAck.Length);

        Assert.True(CompxSessionAck.TryGetModelCode(shifted, out ushort model));
        Assert.Equal(LingbaoPluginModelCode, model);
    }

    [Fact]
    public void L5_K98推送解析出100和放电标志()
    {
        var push = MchoseV1Codec.ParsePush(K98BatteryPush100);
        Assert.Equal(V1PushKind.Battery, push.Kind);
        Assert.Equal(100, push.Percent);
        Assert.Equal((byte)0x01, push.ChargeFlag);
        Assert.Equal(ChargingState.Full,
            ChargingStateMap.FromChargingFlag((push.ChargeFlag & 0x10) != 0, push.Percent));
    }

    /// <summary>与 <c>LingbaoPlugin.K98ModelCode</c> 保持同一个真机值（本测试项目不编 Core 插件类型）。</summary>
    private const ushort LingbaoPluginModelCode = 0x0212;
}
