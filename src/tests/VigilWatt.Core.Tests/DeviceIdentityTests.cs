using VigilWatt.Core.Devices;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Mchose;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 设备身份快照（USB / 协议自报 / 接收器）—— 三者互不覆盖，且已知值不会被 null 洗掉。
/// 2026/11/09 21:20:00 20211
/// 身份只进展示：不参与去重、历史与提醒，故这里同时钉住"身份变化不算设备变化"。
/// </summary>
public class DeviceIdentityTests
{
    private static readonly UsbIdentity Dongle = new(0x3837, 0x1018, "SN-DONGLE");
    private static readonly ReportedIdentity Body = new(0x41E4, 0x2001, "K7 V2");
    private static readonly ReceiverIdentity Transport = new("dongle-wireless", null);

    private static DeviceStatus Device(
        UsbIdentity? usb = null, ReportedIdentity? reported = null,
        ReceiverIdentity? receiver = null, bool online = true) => new()
    {
        Id = "mchose:ABCD",
        Name = "迈从 K7 V2",
        Source = DeviceSource.Hid24G,
        Kind = DeviceKind.Mouse,
        BatteryPercent = 80,
        Charging = ChargingState.Discharging,
        IsOnline = online,
        Usb = usb,
        Reported = reported,
        Receiver = receiver,
    };

    private static HidDeviceInfo Hid(ushort vid, ushort pid, string serial = "") => new()
    {
        DevicePath = $@"\\?\hid#vid_{vid:x4}&pid_{pid:x4}",
        VendorId = vid,
        ProductId = pid,
        SerialNumber = serial,
    };

    // ── 兼容与拷贝 ────────────────────────────────────────────────

    [Fact]
    public void I1_不设身份时三个字段都是null()
    {
        // 旧构造方式（36 个插件、BLE watcher、墓碑）全都不碰新字段，必须保持 null 而不是空结构
        var device = Device();

        Assert.Null(device.Usb);
        Assert.Null(device.Reported);
        Assert.Null(device.Receiver);
    }

    [Fact]
    public void I2_Clone保留三种身份()
    {
        var copy = Device(Dongle, Body, Transport).Clone();

        Assert.Equal(Dongle, copy.Usb!.Value);
        Assert.Equal(Body, copy.Reported!.Value);
        Assert.Equal(Transport, copy.Receiver!.Value);
    }

    [Fact]
    public void I3_Clone不会与源共享可变状态()
    {
        var source = Device(Dongle, Body, Transport);
        var copy = source.Clone();

        copy.Usb = null;
        copy.Reported = null;
        copy.Receiver = null;

        Assert.Equal(Dongle, source.Usb!.Value);
        Assert.Equal(Body, source.Reported!.Value);
        Assert.Equal(Transport, source.Receiver!.Value);
    }

    // ── 合并：不清空、不降级 ──────────────────────────────────────

    [Fact]
    public void I4_轮询应答不带身份时保留已知值()
    {
        var stored = Device(Dongle, Body, Transport);

        DeviceStatusMerger.Apply(stored, Device());

        Assert.Equal(Dongle, stored.Usb!.Value);
        Assert.Equal(Body, stored.Reported!.Value);
        Assert.Equal(Transport, stored.Receiver!.Value);
    }

    [Fact]
    public void I5_离线墓碑不清空身份()
    {
        var stored = Device(Dongle, Body, Transport);

        DeviceStatusMerger.Apply(stored, Device(online: false));

        Assert.False(stored.IsOnline);
        Assert.Equal(Dongle, stored.Usb!.Value);
        Assert.Equal(Body, stored.Reported!.Value);
        Assert.Equal(Transport, stored.Receiver!.Value);
    }

    [Fact]
    public void I6_新身份可以更新旧身份()
    {
        var stored = Device(new UsbIdentity(0x3837, 0x1018, null),
            new ReportedIdentity(0x41E4, 0x2001, null));

        DeviceStatusMerger.Apply(stored,
            Device(new UsbIdentity(0x3837, 0x1018, "SN-2"), new ReportedIdentity(0x41E4, 0x2001, "K7 V2")));

        Assert.Equal("SN-2", stored.Usb!.Value.SerialNumber);
        Assert.Equal("K7 V2", stored.Reported!.Value.Model);
    }

    [Fact]
    public void I7_新快照缺序列号时保留旧序列号()
    {
        var stored = Device(new UsbIdentity(0x3837, 0x1018, "SN-1"));

        // 同一接口重新认领，这一轮没读到 SN：不能把已知 SN 洗成 null
        DeviceStatusMerger.Apply(stored, Device(new UsbIdentity(0x3837, 0x1018, null)));

        Assert.Equal("SN-1", stored.Usb!.Value.SerialNumber);
    }

    [Fact]
    public void I8_自报型号缺失时不把已解析的型号降级()
    {
        var stored = Device(reported: new ReportedIdentity(0x41E4, 0x2001, "K7 V2"));

        // 目录这次没收录 / 设备在睡导致型号没解出来：VID/PID 照常更新，型号保持
        DeviceStatusMerger.Apply(stored, Device(reported: new ReportedIdentity(0x41E4, 0x2001, null)));

        Assert.Equal("K7 V2", stored.Reported!.Value.Model);
    }

    [Fact]
    public void I9_空的接收器身份不清空已知通道()
    {
        var stored = Device(receiver: Transport);

        // default(ReceiverIdentity) 两项都空，语义是"这次没信息"，不是"没有接收器"
        DeviceStatusMerger.Apply(stored, Device(receiver: default));

        Assert.Equal(Transport, stored.Receiver!.Value);
    }

    // ── 迈从：自报身份不拿 dongle PID 冒充 ────────────────────────

    [Fact]
    public void I10_迈从自报身份用本体PID而不是donglePID()
    {
        var identity = MchoseDeviceIdentity.FromUsb(Hid(0x3837, 0x1018))
            .WithReported(0x41E4, 0x2001, "K7 V2");

        var reported = identity.ToReportedIdentity();

        Assert.NotNull(reported);
        Assert.Equal((ushort)0x41E4, reported!.Value.VendorId);
        Assert.Equal((ushort)0x2001, reported.Value.ProductId);
        Assert.Equal("K7 V2", reported.Value.Model);
    }

    [Fact]
    public void I11_迈从没拿到自报身份时不产生快照()
    {
        // 只有 USB 身份时不许编一份自报身份出来——USB 认领到的是 dongle
        Assert.Null(MchoseDeviceIdentity.FromUsb(Hid(0x3837, 0x1018)).ToReportedIdentity());
    }

    [Fact]
    public void I12_迈从目录未收录时型号为null而不是空串()
    {
        var reported = MchoseDeviceIdentity.FromUsb(Hid(0x3837, 0x1018))
            .WithReported(0x3837, 0x9999, "")
            .ToReportedIdentity();

        Assert.NotNull(reported);
        Assert.Null(reported!.Value.Model);
        Assert.False(reported.Value.HasModel);
    }

    [Fact]
    public void I13_本体身份为空时保留之前有效的快照()
    {
        // 设备睡醒后这轮只回了拒绝应答，没有 0x0900：ToReportedIdentity 返回 null，合并必须保住旧值
        var stored = Device(reported: Body);

        DeviceStatusMerger.Apply(stored, Device());

        Assert.Equal(Body, stored.Reported!.Value);
    }

    // ── 接收器身份与本体身份不混淆 ────────────────────────────────

    [Fact]
    public void I14_只有登记过的dongle才有通道身份_本体接口没有()
    {
        Assert.True(MchoseDeviceCatalog.TryGetTransport(0x3837, 0x1018, out var dongle));
        Assert.False(string.IsNullOrWhiteSpace(dongle.Role));

        // 本体 PID 不是传输通道：调用方据此留 null，不会把鼠标本体说成接收器。
        // 0x41E4:0x2001 是 G87 的 dongle（mchose.md §1），本体例改用 A5 的 0x1101。
        Assert.False(MchoseDeviceCatalog.TryGetTransport(0x41E4, 0x1101, out _));
    }

    [Fact]
    public void I26_G87的dongle已登记为传输通道()
    {
        // 主机读的是接收器通道 ⇒ 数据只能经 2.4G 到达；这颗 PID 不登记的话徽标只能显示未知
        Assert.True(MchoseDeviceCatalog.TryGetTransport(0x41E4, 0x2001, out var dongle));
        Assert.Equal("dongle-wireless", dongle.Role);
        Assert.Equal("keyboard-v1", dongle.Protocol);
    }

    [Fact]
    public void I15_三种身份各存各的_互不覆盖()
    {
        var device = Device(Dongle, Body, Transport);

        Assert.Equal((ushort)0x1018, device.Usb!.Value.ProductId);       // 接口 = dongle
        Assert.Equal((ushort)0x2001, device.Reported!.Value.ProductId);  // 自报 = 本体
        Assert.Equal("dongle-wireless", device.Receiver!.Value.Role);    // 通道 = 目录登记的角色
    }

    // ── 不参与去重 / 历史 / 提醒 ──────────────────────────────────

    [Fact]
    public void I16_身份变化不算设备变化()
    {
        var stored = Device();
        var incoming = Device(Dongle, Body, Transport);

        // HasChanged 只看电量/充电/在线：身份变了不该写历史、不该触发提醒
        Assert.False(DeviceChangeDetector.HasChanged(stored, incoming));
        Assert.False(DeviceChangeDetector.HasPointerChanged(stored, incoming));
        Assert.False(DeviceChangeDetector.HasConnectionChanged(stored, incoming));
    }

    [Fact]
    public void I17_展示去重仍按显示名分组_身份不参与()
    {
        var online = Device(Dongle);
        var offline = Device(online: false);

        var result = DeviceDisplayDeduplicator.Dedupe([online, offline]);

        Assert.Single(result);
        Assert.Same(online, result.Single());
    }

    // ── 复核（M4-2R）：换身份不得继承旧子字段 ────────────────────

    [Fact]
    public void I18_无效的USB默认身份不覆盖已知值()
    {
        var stored = Device(Dongle);

        // 调用方漏判、把 default 结构塞进来（VID/PID 都是 0），不能当成"新身份"
        DeviceStatusMerger.Apply(stored, Device(default(UsbIdentity)));

        Assert.Equal(Dongle, stored.Usb!.Value);
        Assert.False(default(UsbIdentity).IsValid);
    }

    [Fact]
    public void I19_USB换了接口不继承旧序列号()
    {
        var stored = Device(new UsbIdentity(0x046D, 0xC548, "SN-BOLT"));

        // 同一品牌、不同 PID：这是另一颗接收器，旧 SN 与它无关
        DeviceStatusMerger.Apply(stored, Device(new UsbIdentity(0x046D, 0xC547, null)));

        Assert.Equal((ushort)0xC547, stored.Usb!.Value.ProductId);
        Assert.Null(stored.Usb.Value.SerialNumber);
    }

    [Fact]
    public void I20_序列号空串统一归一化为null()
    {
        var stored = Device(new UsbIdentity(0x3837, 0x1018, "  "));

        // 构造方给了空白串，语义仍是"没读到"；合并后不得留下空串
        var merged = UsbIdentity.Merge(stored.Usb, new UsbIdentity(0x3837, 0x1018, null));

        Assert.NotNull(merged);
        Assert.Null(merged!.Value.SerialNumber);
    }

    [Fact]
    public void I21_无效的自报默认身份不覆盖已知值()
    {
        var stored = Device(reported: Body);

        DeviceStatusMerger.Apply(stored, Device(reported: default(ReportedIdentity)));

        Assert.Equal(Body, stored.Reported!.Value);
        Assert.False(default(ReportedIdentity).IsValid);
    }

    [Fact]
    public void I22_自报换了PID不继承旧型号()
    {
        var stored = Device(reported: new ReportedIdentity(0x41E4, 0x2001, "MCHOSE A"));

        // 0x2001 → 0x2002 是另一台设备；型号跟着身份走，不能把 A 的型号贴到 B 上
        DeviceStatusMerger.Apply(stored, Device(reported: new ReportedIdentity(0x41E4, 0x2002, null)));

        Assert.Equal((ushort)0x2002, stored.Reported!.Value.ProductId);
        Assert.Null(stored.Reported.Value.Model);
    }

    [Fact]
    public void I23_接收器Role改变不继承旧Family()
    {
        var stored = Device(receiver: new ReceiverIdentity("dongle-wireless", "bolt"));

        // Role 换了就不存在"旧 Family 还适用"的依据，只能整体替换
        DeviceStatusMerger.Apply(stored, Device(receiver: new ReceiverIdentity("dongle-cz", null)));

        Assert.Equal("dongle-cz", stored.Receiver!.Value.Role);
        Assert.Null(stored.Receiver.Value.Family);
    }

    [Fact]
    public void I24_接收器Family改变不继承旧Role()
    {
        var stored = Device(receiver: new ReceiverIdentity("dongle-wireless", "bolt"));

        DeviceStatusMerger.Apply(stored, Device(receiver: new ReceiverIdentity(null, "lightspeed")));

        Assert.Null(stored.Receiver!.Value.Role);
        Assert.Equal("lightspeed", stored.Receiver.Value.Family);
    }

    [Fact]
    public void I25_接收器另一项没变时才沿用缺失项()
    {
        var stored = Device(receiver: new ReceiverIdentity("dongle-wireless", "bolt"));

        // Family 与旧值一致、Role 缺失：这一项可以安全沿用
        DeviceStatusMerger.Apply(stored, Device(receiver: new ReceiverIdentity(null, "bolt")));

        Assert.Equal("dongle-wireless", stored.Receiver!.Value.Role);
        Assert.Equal("bolt", stored.Receiver.Value.Family);
    }

    [Fact]
    public void I27_接收器身份不影响Connection和另外两种身份()
    {
        var stored = Device(Dongle, Body, Transport);
        stored.Connection = TelemetryValue<ConnectionKind>.Reported(
            ConnectionKind.Wireless24G, new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));

        DeviceStatusMerger.Apply(stored, Device(receiver: new ReceiverIdentity(null, "bolt")));

        Assert.Equal("bolt", stored.Receiver!.Value.Family);
        Assert.Null(stored.Receiver.Value.Role);
        Assert.Equal(Dongle, stored.Usb!.Value);
        Assert.Equal(Body, stored.Reported!.Value);
        Assert.Equal(ConnectionKind.Wireless24G, stored.Connection.Value);
        Assert.Equal(TelemetrySource.DeviceReported, stored.Connection.Source);
    }
}
