using VigilWatt.Core.Devices;
using VigilWatt.Core.Models;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： DeviceChangeDetector + DeviceStatus.Clone —— Upsert 变化判定与骤降保护的纯逻辑。
/// 2026/10/10 17:44:33 xliu
/// 回归 CR-03 根因：数据源复用并原地改写同一 DeviceStatus 实例，若不快照就入库， 变化判定会变成"同一对象和自己比"而恒为假（历史即写/UI 刷新/骤降保护全失效）。
/// </summary>
public class DeviceChangeDetectorTests
{
    private static DeviceStatus Device(int? percent, ChargingState charging = ChargingState.Discharging, bool online = true) => new()
    {
        Id = "hid24g:abc",
        Name = "雷柏 Rapoo Gaming Device",
        Source = DeviceSource.Hid24G,
        Kind = DeviceKind.Mouse,
        Protocol = "Rapoo V2",
        BatteryPercent = percent,
        Charging = charging,
        IsOnline = online,
    };

    [Fact]
    public void C1_数据源原地改写_快照能识别出变化()
    {
        // 模拟首建：库里存快照，数据源继续持有自己的实例
        var source = Device(80);
        var stored = source.Clone();

        // 数据源原地改写（这正是插件/watcher 的实际行为）
        source.BatteryPercent = 79;

        Assert.True(DeviceChangeDetector.HasChanged(stored, source));   // 修复前这里恒为 false
        Assert.Equal(80, stored.BatteryPercent);                        // 库里的旧值没被就地污染
    }

    [Fact]
    public void C2_同一实例比较_恒为假_说明为什么必须快照()
    {
        // 反面证据：若不快照，Upsert 里 status 与 incoming 是同一对象，判定必然失效
        var source = Device(80);
        source.BatteryPercent = 79;

        Assert.False(DeviceChangeDetector.HasChanged(source, source));
    }

    [Fact]
    public void C3_Clone是独立副本_改源不影响副本()
    {
        var source = Device(80);
        var clone = source.Clone();

        source.BatteryPercent = 1;
        source.Charging = ChargingState.Charging;
        source.IsOnline = false;
        source.Name = "改名";

        Assert.Equal(80, clone.BatteryPercent);
        Assert.Equal(ChargingState.Discharging, clone.Charging);
        Assert.True(clone.IsOnline);
        Assert.Equal("雷柏 Rapoo Gaming Device", clone.Name);
    }

    [Fact]
    public void C4_充电标志变化_电量不变也算变化()
    {
        // 拔线瞬间电量常停在原值，只靠电量判变化会让 UI 一直显示"充电中"
        var stored = Device(99, ChargingState.Charging);
        var incoming = Device(99, ChargingState.Discharging);

        Assert.True(DeviceChangeDetector.HasChanged(stored, incoming));
    }

    [Fact]
    public void C5_在线状态变化算变化()
    {
        Assert.True(DeviceChangeDetector.HasChanged(Device(50), Device(50, online: false)));
    }

    [Fact]
    public void C6_全字段一致不算变化()
    {
        Assert.False(DeviceChangeDetector.HasChanged(Device(50), Device(50)));
    }

    [Theory]
    [InlineData(80, 1, true)]     // 休眠/关机伪读数：骤降 79 点
    [InlineData(80, 59, true)]    // 骤降 21 点，超过阈值 20
    [InlineData(80, 60, false)]   // 正好 20 点，不算异常
    [InlineData(80, 70, false)]   // 正常掉电
    [InlineData(21, 20, false)]   // 跨阈值附近的正常掉电
    public void C7_骤降保护阈值判定(int previous, int current, bool expected)
    {
        Assert.Equal(expected, DeviceChangeDetector.IsImplausibleDrop(previous, current, 20));
    }

    [Fact]
    public void C8_无上次读数时不算骤降()
    {
        Assert.False(DeviceChangeDetector.IsImplausibleDrop(null, 1, 20));
    }

    // ── 指针遥测（PR-A）────────────────────────────────────────────

    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static DeviceStatus WithDpi(int dpi, DateTimeOffset at, TelemetrySource source = TelemetrySource.DeviceReported)
    {
        var device = Device(80);
        device.Pointer = new PointerTelemetry
        {
            DpiX = source == TelemetrySource.DeviceReported
                ? TelemetryValue<int>.Reported(dpi, at)
                : TelemetryValue<int>.Inferred(dpi, at),
        };
        return device;
    }

    [Fact]
    public void C9_Clone复制指针遥测()
    {
        var source = WithDpi(1600, T0);
        var clone = source.Clone();

        source.Pointer = WithDpi(3200, T0).Pointer;

        Assert.Equal(1600, clone.Pointer.DpiX.Value);
        Assert.Equal(T0, clone.Pointer.DpiX.ObservedAt);
    }

    [Fact]
    public void C10_指针值变化算变化()
    {
        Assert.True(DeviceChangeDetector.HasPointerChanged(WithDpi(1600, T0), WithDpi(3200, T0)));
    }

    [Fact]
    public void C11_只有观测时间前进不算变化()
    {
        Assert.False(DeviceChangeDetector.HasPointerChanged(
            WithDpi(1600, T0), WithDpi(1600, T0 + TimeSpan.FromSeconds(3))));
    }

    [Fact]
    public void C12_来源变化算变化()
    {
        Assert.True(DeviceChangeDetector.HasPointerChanged(
            WithDpi(1600, T0, TelemetrySource.Inferred), WithDpi(1600, T0)));
    }

    [Fact]
    public void C13_incoming没带指针信息不算变化()
    {
        Assert.False(DeviceChangeDetector.HasPointerChanged(WithDpi(1600, T0), Device(80)));
        Assert.False(DeviceChangeDetector.HasPointerChanged(Device(80), Device(80)));
    }

    [Fact]
    public void C14_指针变化不进入电量变化判定()
    {
        // 这条钉住"DPI 变化不写电量历史"：HasChanged 只认电量/充电/在线
        Assert.False(DeviceChangeDetector.HasChanged(WithDpi(1600, T0), WithDpi(3200, T0)));
    }
}
