using VigilWatt.Core.Models;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 连接方式的唯一展示口径——有值按链路，无值只允许蓝牙/本机兜底，Hid24G 一律"未知"。
/// 2026/11/09 21:52:00 20211
/// 对应 M4-3A 审计 P1/P3：来源是接入栈，不能冒充实际链路；未知也不许借 2.4G 的颜色。
/// </summary>
public class DeviceConnectionInfoTests
{
    private static TelemetryValue<ConnectionKind> Value(ConnectionKind kind, TelemetrySource source = TelemetrySource.DeviceReported)
        => source == TelemetrySource.Inferred
            ? TelemetryValue<ConnectionKind>.Inferred(kind, DateTimeOffset.Now)
            : TelemetryValue<ConnectionKind>.Reported(kind, DateTimeOffset.Now);

    [Fact]
    public void D1_三种链路各有标签()
    {
        Assert.Equal("有线", DeviceConnectionInfo.Label(Value(ConnectionKind.Wired), DeviceSource.Hid24G));
        Assert.Equal("2.4G", DeviceConnectionInfo.Label(Value(ConnectionKind.Wireless24G), DeviceSource.Hid24G));
        Assert.Equal("蓝牙", DeviceConnectionInfo.Label(Value(ConnectionKind.Bluetooth), DeviceSource.Hid24G));
    }

    [Fact]
    public void D2_无值时Hid24G显示未知而不是2_4G()
    {
        // 修前：BadgeLabel(Unknown, Hid24G) == "2.4G"，把接入栈当成了链路
        Assert.Equal("未知", DeviceConnectionInfo.Label(default, DeviceSource.Hid24G));
    }

    [Fact]
    public void D3_蓝牙与本机可以按来源兜底()
    {
        Assert.Equal("蓝牙", DeviceConnectionInfo.Label(default, DeviceSource.Ble));
        Assert.Equal("蓝牙", DeviceConnectionInfo.Label(default, DeviceSource.BluetoothClassic));
        Assert.Equal("本机", DeviceConnectionInfo.Label(default, DeviceSource.System));
    }

    [Fact]
    public void D4_值为Unknown时不能变成有效链路()
    {
        // 防御：即便有人把 Unknown 包成"有来源"，也只能显示未知，不许落到 2.4G
        Assert.Equal("未知", DeviceConnectionInfo.Label(Value(ConnectionKind.Unknown), DeviceSource.Hid24G));
    }

    [Fact]
    public void D5_未知不再与2_4G共用取色档()
    {
        Assert.Equal(ConnectionTone.Unknown, DeviceConnectionInfo.Tone(default, DeviceSource.Hid24G));
        Assert.NotEqual(ConnectionTone.Wireless24G, DeviceConnectionInfo.Tone(default, DeviceSource.Hid24G));

        Assert.Equal(ConnectionTone.Bluetooth, DeviceConnectionInfo.Tone(default, DeviceSource.Ble));
        Assert.Equal(ConnectionTone.System, DeviceConnectionInfo.Tone(default, DeviceSource.System));
        Assert.Equal(ConnectionTone.Wired, DeviceConnectionInfo.Tone(Value(ConnectionKind.Wired), DeviceSource.Hid24G));
    }

    [Fact]
    public void D6_在线只报链路_离线区分最后已知与未知()
    {
        Assert.Equal("有线", DeviceConnectionInfo.Describe(Value(ConnectionKind.Wired), DeviceSource.Hid24G, isOnline: true));
        Assert.Equal("离线 · 最后已知：有线", DeviceConnectionInfo.Describe(Value(ConnectionKind.Wired), DeviceSource.Hid24G, isOnline: false));

        // 没有值就不写"最后已知"：暗示曾经测到过是没有证据的
        Assert.Equal("离线 · 未知", DeviceConnectionInfo.Describe(default, DeviceSource.Hid24G, isOnline: false));
        Assert.Equal("离线 · 蓝牙", DeviceConnectionInfo.Describe(default, DeviceSource.Ble, isOnline: false));
    }

    [Fact]
    public void D7_推断值也照常显示_但来源仍可区分()
    {
        var inferred = Value(ConnectionKind.Wireless24G, TelemetrySource.Inferred);

        Assert.Equal("2.4G", DeviceConnectionInfo.Label(inferred, DeviceSource.Hid24G));
        Assert.Equal(TelemetrySource.Inferred, inferred.Source);
        Assert.True(inferred.HasValue);
    }

    [Fact]
    public void D8_带来源的Unknown仍然显示未知()
    {
        // HasValue 为真，但 Unknown 不是一条链路：文案、取色、离线描述都必须当"没有信息"处理
        var reportedUnknown = Value(ConnectionKind.Unknown);

        Assert.Equal("未知", DeviceConnectionInfo.Label(reportedUnknown, DeviceSource.Hid24G));
        Assert.Equal(ConnectionTone.Unknown, DeviceConnectionInfo.Tone(reportedUnknown, DeviceSource.Hid24G));
        Assert.Equal("离线 · 未知", DeviceConnectionInfo.Describe(reportedUnknown, DeviceSource.Hid24G, isOnline: false));
    }

    [Fact]
    public void D9_体系标签只读目录展示名()
    {
        // 展示层不认识任何品牌：名字由目录给，给了就显示，没给就不显示
        Assert.Equal("Bolt", DeviceConnectionInfo.ReceiverLabel(new ReceiverIdentity(null, "bolt", "Bolt")));
        Assert.Null(DeviceConnectionInfo.ReceiverLabel(new ReceiverIdentity(null, "bolt")));
        Assert.Null(DeviceConnectionInfo.ReceiverLabel(new ReceiverIdentity(null, "bolt", "   ")));
        Assert.Null(DeviceConnectionInfo.ReceiverLabel(null));
    }

    [Fact]
    public void D10_体系图标key只读目录声明_缺失回退wireless()
    {
        Assert.Equal("wireless", DeviceConnectionInfo.ReceiverIconKey(new ReceiverIdentity(null, "bolt", "Bolt", "wireless")));
        // key 归一成小写：目录写 "LIGHTNING" 与 "lightning" 是同一张图
        Assert.Equal("lightning", DeviceConnectionInfo.ReceiverIconKey(new ReceiverIdentity(null, "lightspeed", "LIGHTSPEED", "LIGHTNING")));
        // 目录漏填图标时不能整块消失，回退信号弧
        Assert.Equal(DeviceConnectionInfo.DefaultReceiverIconKey,
            DeviceConnectionInfo.ReceiverIconKey(new ReceiverIdentity(null, "bolt", "Bolt")));
        // 没有展示名就没有标签，也就不该有孤立图标
        Assert.Null(DeviceConnectionInfo.ReceiverIconKey(new ReceiverIdentity(null, "bolt", null, "wireless")));
        Assert.Null(DeviceConnectionInfo.ReceiverIconKey(null));
    }
}
