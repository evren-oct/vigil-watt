using VigilWatt.Core.Devices;
using VigilWatt.Core.Models;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： DeviceStatusMerger —— Upsert 的原地合并规则。
/// 2026/10/06 22:54:19 xliu
/// 核心场景是"墓碑被真实来源接管"：墓碑按历史名字猜出的 Kind 全是 Unknown，设备上线后 插件声明的 Kind/Protocol 必须能覆盖它，否则图标在进程内冻结。
/// </summary>
public class DeviceStatusMergerTests
{
    private static DeviceStatus Tombstone(string name = "雷柏 Rapoo Gaming Device") => new()
    {
        Id = "rapoo:123",
        Name = name,
        Source = DeviceSource.Hid24G,
        // RestoreOfflineTombstones 按名字猜："雷柏 Rapoo Gaming Device" 无关键字 → Unknown
        Kind = DeviceKind.Unknown,
        Protocol = "历史记录",
        BatteryPercent = null,
        Charging = ChargingState.Unknown,
        LastUpdated = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero),
        IsOnline = false,
    };

    [Fact]
    public void M1_墓碑被插件接管_声明的Kind和Protocol覆盖猜测()
    {
        var status = Tombstone();
        var incoming = new DeviceStatus
        {
            Id = "rapoo:123",
            Name = "雷柏 Rapoo Gaming Device",
            Source = DeviceSource.Hid24G,
            Kind = DeviceKind.Mouse,           // 插件声明
            Protocol = "Rapoo V2",
            BatteryPercent = 80,
            Charging = ChargingState.Discharging,
            LastUpdated = new DateTimeOffset(2026, 10, 6, 20, 0, 0, TimeSpan.Zero),
            IsOnline = true,
        };

        DeviceStatusMerger.Apply(status, incoming);

        Assert.Equal(DeviceKind.Mouse, status.Kind);        // 图标修复的关键断言
        Assert.Equal("Rapoo V2", status.Protocol);          // "历史记录"不得残留
        Assert.Equal(80, status.BatteryPercent);
        Assert.True(status.IsOnline);
        Assert.Equal(incoming.LastUpdated, status.LastUpdated);
    }

    [Fact]
    public void M2_墓碑猜对的Kind不被Unknown洗掉()
    {
        var status = Tombstone("北通 T9");   // 名字带关键字，墓碑猜成手柄
        status.Kind = DeviceKind.Gamepad;
        var incoming = new DeviceStatus
        {
            Id = "rapoo:123",
            Name = "北通 T9",
            Source = DeviceSource.Hid24G,
            Kind = DeviceKind.Unknown,      // BLE 监视器这类不声明类型的来源
            Protocol = "",
            BatteryPercent = 50,
            LastUpdated = new DateTimeOffset(2026, 10, 6, 20, 0, 0, TimeSpan.Zero),
            IsOnline = true,
        };

        DeviceStatusMerger.Apply(status, incoming);

        Assert.Equal(DeviceKind.Gamepad, status.Kind);
        Assert.Equal("历史记录", status.Protocol);   // 空串视为"未声明"，不覆盖
    }

    [Fact]
    public void M3_新设备即incoming_合并后字段不变()
    {
        // Upsert 首建时 status 与 incoming 是同一实例，Apply 不得破坏任何字段
        var incoming = new DeviceStatus
        {
            Id = "mchose:456",
            Name = "MCHOSE 2.4G Wireless",
            Source = DeviceSource.Hid24G,
            Kind = DeviceKind.Keyboard,
            Protocol = "MCHOSE V2",
            BatteryPercent = 66,
            LastUpdated = new DateTimeOffset(2026, 10, 6, 20, 0, 0, TimeSpan.Zero),
            IsOnline = true,
        };

        DeviceStatusMerger.Apply(incoming, incoming);

        Assert.Equal(DeviceKind.Keyboard, incoming.Kind);
        Assert.Equal("MCHOSE V2", incoming.Protocol);
        Assert.Equal(66, incoming.BatteryPercent);
        Assert.True(incoming.IsOnline);
    }

    // ── 指针遥测（PR-A）────────────────────────────────────────────

    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static DeviceStatus WithPointer(PointerTelemetry pointer) => new()
    {
        Id = "rapoo:1",
        Name = "雷柏 VT3",
        Source = DeviceSource.Hid24G,
        Kind = DeviceKind.Mouse,
        BatteryPercent = 80,
        Charging = ChargingState.Discharging,
        IsOnline = true,
        Pointer = pointer,
    };

    [Fact]
    public void M4_incoming没带指针信息时保留已知遥测()
    {
        var stored = WithPointer(new PointerTelemetry
        {
            DpiX = TelemetryValue<int>.Reported(1600, T0),
            ReportRateSettingHz = TelemetryValue<int>.Reported(1000, T0),
        });
        var incoming = WithPointer(default);

        DeviceStatusMerger.Apply(stored, incoming);

        Assert.Equal(1600, stored.Pointer.DpiX.Value);
        Assert.Equal(1000, stored.Pointer.ReportRateSettingHz.Value);
        Assert.Equal(T0, stored.Pointer.DpiX.ObservedAt);
    }

    [Fact]
    public void M5_空遥测被首次观测填上()
    {
        var stored = WithPointer(default);
        var incoming = WithPointer(new PointerTelemetry { DpiX = TelemetryValue<int>.Reported(3200, T0) });

        DeviceStatusMerger.Apply(stored, incoming);

        Assert.Equal(3200, stored.Pointer.DpiX.Value);
    }

    [Fact]
    public void M6_部分更新只改带值的那一项()
    {
        var stored = WithPointer(new PointerTelemetry
        {
            DpiX = TelemetryValue<int>.Reported(1600, T0),
            ReportRateSettingHz = TelemetryValue<int>.Reported(1000, T0),
        });
        var incoming = WithPointer(new PointerTelemetry
        {
            DpiLevel = TelemetryValue<int>.Reported(3, T0 + TimeSpan.FromSeconds(3)),
        });

        DeviceStatusMerger.Apply(stored, incoming);

        Assert.Equal(3, stored.Pointer.DpiLevel.Value);
        Assert.Equal(1600, stored.Pointer.DpiX.Value);          // 没带的项原样保留
        Assert.Equal(1000, stored.Pointer.ReportRateSettingHz.Value);
    }

    [Fact]
    public void M8_连接方式未知不覆盖已知()
    {
        var stored = WithPointer(default);
        stored.Connection = ConnectionKind.Wired;

        // 轮询 / 离线墓碑都不带这个字段（默认 Unknown），不能把已知的「有线」洗回未知
        DeviceStatusMerger.Apply(stored, WithPointer(default));
        Assert.Equal(ConnectionKind.Wired, stored.Connection);

        var incoming = WithPointer(default);
        incoming.Connection = ConnectionKind.Wireless24G;
        DeviceStatusMerger.Apply(stored, incoming);
        Assert.Equal(ConnectionKind.Wireless24G, stored.Connection);
    }

    [Fact]
    public void M7_电量仍是无条件覆盖()
    {
        // 既有语义（PR-2 的"null 不覆盖"落在 TryApplyReading，不在 merger）：这里必须照旧被清成 null
        var stored = WithPointer(default);
        var incoming = WithPointer(default);
        incoming.BatteryPercent = null;

        DeviceStatusMerger.Apply(stored, incoming);

        Assert.Null(stored.BatteryPercent);
    }
}
