using VigilWatt.Core.Devices;
using VigilWatt.Core.Models;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： PointerTelemetry / TelemetryValue 的纯逻辑：来源、合并、过期与"值变"判定。
/// 2026/11/03 22:10:00 xliu
/// P14/P15 是"时间未知/未来时间不判过期"的回归，P11 是"ObservedAt 不参与值变判定"的回归。
/// </summary>
public class PointerTelemetryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static PointerTelemetry WithDpi(int dpi, DateTimeOffset at) => new()
    {
        DpiX = TelemetryValue<int>.Reported(dpi, at),
    };

    // ── 未知与来源 ────────────────────────────────────────────────

    [Fact]
    public void P1_Unknown的值为null且不可信()
    {
        var unknown = TelemetryValue<int>.Unknown;

        Assert.Null(unknown.Value);
        Assert.Equal(TelemetrySource.Unknown, unknown.Source);
        Assert.False(unknown.HasValue);
        Assert.Equal(default, unknown.ObservedAt);
    }

    [Fact]
    public void P2_四个工厂方法各自带对来源()
    {
        Assert.Equal(TelemetrySource.DeviceReported, TelemetryValue<int>.Reported(1, T0).Source);
        Assert.Equal(TelemetrySource.Inferred, TelemetryValue<int>.Inferred(1, T0).Source);
        Assert.Equal(TelemetrySource.Estimated, TelemetryValue<int>.Estimated(1, T0).Source);
        Assert.Equal(TelemetrySource.VendorSdk, TelemetryValue<int>.FromVendorSdk(1, T0).Source);
        Assert.Equal(T0, TelemetryValue<int>.Reported(1, T0).ObservedAt);
    }

    [Fact]
    public void P3_真实的0不能被当成未知()
    {
        var zero = TelemetryValue<int>.Reported(0, T0);

        Assert.True(zero.HasValue);
        Assert.Equal(0, zero.Value);
    }

    // ── 过期 ──────────────────────────────────────────────────────

    [Fact]
    public void P4_过期边界恰好等于阈值不算过期()
    {
        var value = TelemetryValue<int>.Reported(1600, T0);
        var maxAge = TimeSpan.FromMinutes(5);

        Assert.False(value.IsStale(T0 + maxAge, maxAge));
        Assert.True(value.IsStale(T0 + maxAge + TimeSpan.FromSeconds(1), maxAge));
    }

    [Fact]
    public void P5_无值永不判过期()
    {
        Assert.False(TelemetryValue<int>.Unknown.IsStale(T0, TimeSpan.FromMinutes(5)));
        Assert.False(PointerTelemetry.Empty.IsStale(T0));
    }

    [Fact]
    public void P13_负阈值抛参数越界()
    {
        var value = TelemetryValue<int>.Reported(1600, T0);

        Assert.Throws<ArgumentOutOfRangeException>(() => value.IsStale(T0, TimeSpan.FromSeconds(-1)));
        // 整块版本必须先校验参数，再看有没有值：空块也要抛
        Assert.Throws<ArgumentOutOfRangeException>(() => PointerTelemetry.Empty.IsStale(T0, TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WithDpi(1600, T0).IsStale(T0, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void P14_观测时间未知时不判过期()
    {
        var value = TelemetryValue<int>.Reported(1600, default);

        Assert.True(value.HasValue);                       // 值本身是可信的
        Assert.False(value.IsStale(T0, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void P15_观测时间在未来时不判过期()
    {
        var value = TelemetryValue<int>.Reported(1600, T0 + TimeSpan.FromMinutes(1));

        Assert.False(value.IsStale(T0, TimeSpan.FromMinutes(5)));   // 时钟回拨：差值为负
    }

    // ── 合并 ──────────────────────────────────────────────────────

    [Fact]
    public void P6_合并时有值的一方连来源与时刻一起覆盖()
    {
        var stored = TelemetryValue<int>.Inferred(800, T0);
        var incoming = TelemetryValue<int>.Reported(1600, T0 + TimeSpan.FromSeconds(3));

        var merged = TelemetryValue<int>.Merge(stored, incoming);

        Assert.Equal(1600, merged.Value);
        Assert.Equal(TelemetrySource.DeviceReported, merged.Source);
        Assert.Equal(T0 + TimeSpan.FromSeconds(3), merged.ObservedAt);
    }

    [Fact]
    public void P7_合并时无值不覆盖已知值()
    {
        var stored = TelemetryValue<int>.Reported(1600, T0);

        var merged = TelemetryValue<int>.Merge(stored, TelemetryValue<int>.Unknown);

        Assert.Equal(1600, merged.Value);
        Assert.Equal(T0, merged.ObservedAt);       // 时间也不能被冲掉
    }

    [Fact]
    public void P8_空值合并等于取incoming()
    {
        var incoming = WithDpi(3200, T0);

        Assert.Equal(incoming, PointerTelemetry.Merge(PointerTelemetry.Empty, incoming));
    }

    [Fact]
    public void P9_最新观测时间取有值项的最大值()
    {
        var telemetry = new PointerTelemetry
        {
            DpiX = TelemetryValue<int>.Reported(1600, T0),
            ReportRateSettingHz = TelemetryValue<int>.Reported(1000, T0 + TimeSpan.FromSeconds(9)),
        };

        Assert.True(telemetry.HasAny);
        Assert.Equal(T0 + TimeSpan.FromSeconds(9), telemetry.LatestObservedAt);
        Assert.Null(PointerTelemetry.Empty.LatestObservedAt);
    }

    [Fact]
    public void P10_部分字段陈旧不算整块过期()
    {
        var telemetry = new PointerTelemetry
        {
            DpiX = TelemetryValue<int>.Reported(1600, T0),                                  // 很旧
            DpiY = TelemetryValue<int>.Reported(1600, T0 + TimeSpan.FromMinutes(10)),       // 刚读
        };

        Assert.False(telemetry.IsStale(T0 + TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5)));
        Assert.True(telemetry.DpiX.IsStale(T0 + TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5)));
    }

    // ── 值变判定 ──────────────────────────────────────────────────

    [Fact]
    public void P11_只有观测时间前进不算值变()
    {
        var stored = WithDpi(1600, T0);
        var incoming = WithDpi(1600, T0 + TimeSpan.FromSeconds(3));

        Assert.False(PointerTelemetry.ValueDiffers(stored, incoming));
    }

    [Fact]
    public void P12_空patch不算值变()
    {
        Assert.False(PointerTelemetry.ValueDiffers(WithDpi(1600, T0), PointerTelemetry.Empty));
        Assert.False(PointerTelemetry.ValueDiffers(PointerTelemetry.Empty, PointerTelemetry.Empty));
    }

    // ── 离线与过期是两个维度（D5/D6）───────────────────────────────

    private static DeviceStatus Device(int? percent, bool online = true) => new()
    {
        Id = "hid24g:pointer",
        Name = "雷柏 VT3",
        Source = DeviceSource.Hid24G,
        Kind = DeviceKind.Mouse,
        BatteryPercent = percent,
        Charging = ChargingState.Discharging,
        IsOnline = online,
    };

    [Fact]
    public void D5_刚离线时遥测保留且仍然新鲜()
    {
        var stored = Device(80);
        stored.Pointer = WithDpi(1600, T0);

        // 离线来源：IsOnline=false 且没带任何指针信息
        var incoming = Device(null, online: false);
        DeviceStatusMerger.Apply(stored, incoming);

        Assert.Equal(1600, stored.Pointer.DpiX.Value);                  // 值保留
        Assert.False(stored.Pointer.IsStale(T0 + TimeSpan.FromSeconds(90)));   // 90s < 5min，仍新鲜
    }

    [Fact]
    public void D6_超过阈值后变陈旧但值不丢()
    {
        var stored = Device(80);
        stored.Pointer = WithDpi(1600, T0);

        DeviceStatusMerger.Apply(stored, Device(null, online: false));

        Assert.Equal(1600, stored.Pointer.DpiX.Value);
        Assert.True(stored.Pointer.IsStale(T0 + TimeSpan.FromMinutes(6)));
    }
}
