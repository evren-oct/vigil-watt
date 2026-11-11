using VigilWatt.Core.Devices;
using VigilWatt.Core.Models;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： PointerTelemetryFormatter —— 设备卡片上 DPI / 档位 / 回报率那行的文案口径（PR-D）。
/// 2026/11/07 18:40:00 xliu
/// 展示状态全在这里钉住：设备页只绑定结果，文案错了必须能在这个文件里复现。
/// </summary>
public class PointerTelemetryFormatterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>观测后 30 秒：远在 DefaultMaxAge（5 分钟）之内。</summary>
    private static readonly DateTimeOffset Now = T0.AddSeconds(30);

    private static DeviceStatus Device(
        PointerTelemetry pointer,
        params (DeviceCapability Capability, CapabilityState State)[] caps)
        => new()
        {
            Id = "rapoo:test",
            Name = "雷柏 VT3",
            Source = DeviceSource.Hid24G,
            BatteryPercent = 87,
            Pointer = pointer,
            Capabilities = CapabilitySet.Create(
                caps.Select(c => new KeyValuePair<DeviceCapability, CapabilityState>(c.Capability, c.State))),
        };

    /// <summary>VT3 0x07 心跳那套字段：DPI 两轴同值、档位索引、回报率设置值，全部 DeviceReported。</summary>
    private static PointerTelemetry Vt3Frame(int dpi = 1600, int level = 4, int rateHz = 1000) => new()
    {
        DpiX = TelemetryValue<int>.Reported(dpi, T0),
        DpiY = TelemetryValue<int>.Reported(dpi, T0),
        DpiLevel = TelemetryValue<int>.Reported(level, T0),
        ReportRateSettingHz = TelemetryValue<int>.Reported(rateHz, T0),
    };

    // ── 有值 ──────────────────────────────────────────────────────

    [Fact]
    public void PF1_有效的DPI档位与回报率各占一段()
    {
        var device = Device(Vt3Frame(),
            (DeviceCapability.DpiRead, CapabilityState.Supported),
            (DeviceCapability.DpiLevelRead, CapabilityState.Supported),
            (DeviceCapability.RateRead, CapabilityState.Supported));

        Assert.Equal("DPI 1600 · 档位 4 · 回报率 1000 Hz", PointerTelemetryFormatter.Summary(device, Now));
    }

    [Fact]
    public void PF2_两轴不一致时不拿任一轴代表两轴()
    {
        var pointer = Vt3Frame() with { DpiY = TelemetryValue<int>.Reported(1200, T0) };

        Assert.StartsWith("DPI 1600×1200", PointerTelemetryFormatter.Summary(Device(pointer), Now));
    }

    [Fact]
    public void PF3_只有一轴上报时按该轴显示()
    {
        var pointer = new PointerTelemetry { DpiX = TelemetryValue<int>.Reported(1600, T0) };

        Assert.Equal("DPI 1600", PointerTelemetryFormatter.Summary(Device(pointer), Now));
    }

    // ── 未获取 ────────────────────────────────────────────────────

    [Fact]
    public void PF4_能力已支持但没读到时显示未获取()
    {
        var device = Device(PointerTelemetry.Empty,
            (DeviceCapability.DpiRead, CapabilityState.Supported),
            (DeviceCapability.DpiLevelRead, CapabilityState.Supported));

        Assert.Equal("DPI 未获取 · 档位 未获取", PointerTelemetryFormatter.Summary(device, Now));
    }

    [Fact]
    public void PF5_未支持与未验证的能力不会显示成已支持()
    {
        var device = Device(PointerTelemetry.Empty,
            (DeviceCapability.DpiRead, CapabilityState.Unsupported),
            (DeviceCapability.DpiLevelRead, CapabilityState.Unverified));

        Assert.Null(PointerTelemetryFormatter.Summary(device, Now));
    }

    [Fact]
    public void PF6_能力为未验证但有值时照常显示()
    {
        // VT3 的 rateRead 就是这一态：解析有值、证据不足。按用户裁定不挂任何标记。
        var device = Device(Vt3Frame(), (DeviceCapability.RateRead, CapabilityState.Unverified));

        Assert.Equal("DPI 1600 · 档位 4 · 回报率 1000 Hz", PointerTelemetryFormatter.Summary(device, Now));
    }

    // ── 档位与 DPI 不混 ───────────────────────────────────────────

    [Fact]
    public void PF7_档位索引不会被当成DPI数值()
    {
        var pointer = new PointerTelemetry { DpiLevel = TelemetryValue<int>.Reported(4, T0) };

        string? text = PointerTelemetryFormatter.Summary(
            Device(pointer, (DeviceCapability.DpiLevelRead, CapabilityState.Supported)), Now);

        Assert.Equal("档位 4", text);
        Assert.DoesNotContain("DPI", text);
    }

    [Fact]
    public void PF8_档位总数未知时不自行补分母()
    {
        var pointer = Vt3Frame() with { DpiLevelCount = TelemetryValue<int>.Unknown };

        Assert.Contains("档位 4", PointerTelemetryFormatter.Summary(Device(pointer), Now));
        Assert.DoesNotContain("档位 4/", PointerTelemetryFormatter.Summary(Device(pointer), Now));
    }

    // ── 来源标记 ──────────────────────────────────────────────────

    [Fact]
    public void PF9_设备直报不打来源标记()
    {
        string? text = PointerTelemetryFormatter.Summary(Device(Vt3Frame()), Now);

        Assert.DoesNotContain("（", text);
    }

    [Fact]
    public void PF10_系统估算的回报率必须带估算标记()
    {
        var pointer = new PointerTelemetry
        {
            ReportRateMeasuredHz = TelemetryValue<int>.Estimated(1000, T0),
        };

        string? text = PointerTelemetryFormatter.Summary(Device(pointer), Now);

        Assert.Equal("回报率 ≈1000 Hz（估算）", text);
        // 关键回归：估算值不得伪装成"设备保存的设置值"
        Assert.NotEqual("回报率 1000 Hz", text);
    }

    [Fact]
    public void PF11_推断值与厂商SDK值各有标记()
    {
        var inferred = new PointerTelemetry { DpiX = TelemetryValue<int>.Inferred(800, T0) };
        var vendor = new PointerTelemetry { DpiX = TelemetryValue<int>.FromVendorSdk(800, T0) };

        Assert.Equal("DPI 800（推断）", PointerTelemetryFormatter.Summary(Device(inferred), Now));
        Assert.Equal("DPI 800（厂商）", PointerTelemetryFormatter.Summary(Device(vendor), Now));
    }

    [Fact]
    public void PF12_设置值优先于实测值且不被替代()
    {
        var pointer = Vt3Frame() with
        {
            ReportRateMeasuredHz = TelemetryValue<int>.Estimated(1008, T0),
        };

        string? text = PointerTelemetryFormatter.Summary(Device(pointer), Now);

        Assert.Equal("DPI 1600 · 档位 4 · 回报率 1000 Hz", text);
        Assert.DoesNotContain("≈", text);
    }

    // ── 过期与离线 ────────────────────────────────────────────────

    [Fact]
    public void PF13_过期的值回落到未获取()
    {
        var device = Device(Vt3Frame(), (DeviceCapability.DpiRead, CapabilityState.Supported));
        var later = T0.Add(PointerTelemetry.DefaultMaxAge).Add(TimeSpan.FromSeconds(1));

        // 过期后三项都不可信：唯一声明过能力的 DPI 说"未获取"，没声明能力的档位/回报率直接不出现
        Assert.Equal("DPI 未获取", PointerTelemetryFormatter.Summary(device, later));
    }

    [Fact]
    public void PF14_离线设备整行不渲染()
    {
        var device = Device(Vt3Frame());
        device.IsOnline = false;

        Assert.Null(PointerTelemetryFormatter.Summary(device, Now));
    }

    // ── 不影响既有展示 ────────────────────────────────────────────

    [Fact]
    public void PF15_没有指针遥测的设备不渲染任何东西()
    {
        var device = Device(PointerTelemetry.Empty);

        Assert.Null(PointerTelemetryFormatter.Summary(device, Now));
        Assert.Equal(87, device.BatteryPercent);
    }

    [Fact]
    public void PF16_摘要里不含电量信息()
    {
        string? text = PointerTelemetryFormatter.Summary(Device(Vt3Frame()), Now);

        Assert.DoesNotContain("%", text);
    }

    [Fact]
    public void PF17_能力集合在Clone后保留()
    {
        var device = Device(PointerTelemetry.Empty, (DeviceCapability.DpiRead, CapabilityState.Supported));

        Assert.Equal(CapabilityState.Supported, device.Clone().Capabilities.Of(DeviceCapability.DpiRead));
    }

    [Fact]
    public void PF18_合并时空能力不覆盖已声明()
    {
        var stored = Device(PointerTelemetry.Empty, (DeviceCapability.DpiRead, CapabilityState.Supported));
        var incoming = Device(PointerTelemetry.Empty);   // 离线墓碑之类没有能力来源

        DeviceStatusMerger.Apply(stored, incoming);

        Assert.Equal(CapabilityState.Supported, stored.Capabilities.Of(DeviceCapability.DpiRead));
    }

    // ── 分段（UI 按 Kind 取标签色，按 IsKnown 决定上不上色）────────

    [Fact]
    public void PM1_分段顺序固定为DPI档位回报率()
    {
        var metrics = PointerTelemetryFormatter.Metrics(Device(Vt3Frame()), Now);

        Assert.Equal(
            new[] { PointerMetricKind.Dpi, PointerMetricKind.Level, PointerMetricKind.Rate },
            metrics.Select(m => m.Kind));
        Assert.All(metrics, m => Assert.True(m.IsKnown));
        Assert.Equal("1600", metrics[0].Value);
        Assert.Equal("1000 Hz", metrics[2].Value);
    }

    [Fact]
    public void PM2_未获取的分段标记为未知且值为未获取()
    {
        var device = Device(PointerTelemetry.Empty, (DeviceCapability.DpiRead, CapabilityState.Supported));
        var metric = Assert.Single(PointerTelemetryFormatter.Metrics(device, Now));

        Assert.Equal(PointerMetricKind.Dpi, metric.Kind);
        Assert.Equal(PointerTelemetryFormatter.UnknownText, metric.Value);
        Assert.False(metric.IsKnown);   // UI 据此灰显、不上色
    }

    [Fact]
    public void PM3_没声明能力的设备不产生任何分段()
    {
        // 键盘 / 耳机 / 本机电量：能力集为空且没有遥测，卡片不该多出一行
        Assert.Empty(PointerTelemetryFormatter.Metrics(Device(PointerTelemetry.Empty), Now));
    }

    [Fact]
    public void PM4_离线设备没有分段()
    {
        var device = Device(Vt3Frame());
        device.IsOnline = false;

        Assert.Empty(PointerTelemetryFormatter.Metrics(device, Now));
    }

    // ── 详情页口径（DetailMetrics）──────────────────────────────
    // 与列表口径的唯一差别：离线仍展示最后已知值，但必须标注时刻与"最后已知"。

    [Fact]
    public void DM1_在线有值带来源与时刻()
    {
        var device = Device(Vt3Frame(), (DeviceCapability.DpiRead, CapabilityState.Supported));
        var dpi = PointerTelemetryFormatter.DetailMetrics(device, Now)[0];

        Assert.True(dpi.IsKnown);
        Assert.Equal("1600", dpi.Value);
        Assert.Equal("设备上报 · 12:00:00", dpi.Note);
    }

    [Fact]
    public void DM2_离线仍展示最后已知值但标明时刻()
    {
        var device = Device(Vt3Frame(), (DeviceCapability.DpiRead, CapabilityState.Supported));
        device.IsOnline = false;

        var dpi = PointerTelemetryFormatter.DetailMetrics(device, Now)[0];

        Assert.True(dpi.IsKnown);
        Assert.Equal("1600", dpi.Value);
        Assert.Equal("最后已知 · 11-07 12:00", dpi.Note);
    }

    [Fact]
    public void DM3_在线但已过期时标注已过期()
    {
        var device = Device(Vt3Frame(), (DeviceCapability.DpiRead, CapabilityState.Supported));

        // DefaultMaxAge = 5 分钟，这里推到 10 分钟
        var dpi = PointerTelemetryFormatter.DetailMetrics(device, T0.AddMinutes(10))[0];

        Assert.True(dpi.IsKnown);
        Assert.Equal("已过期 · 11-07 12:00", dpi.Note);
    }

    [Fact]
    public void DM4_能力已支持但没读到时不写最后已知()
    {
        var device = Device(PointerTelemetry.Empty, (DeviceCapability.RateRead, CapabilityState.Supported));
        var rate = PointerTelemetryFormatter.DetailMetrics(device, Now)[0];

        Assert.False(rate.IsKnown);
        Assert.Equal(PointerTelemetryFormatter.UnknownText, rate.Value);
        Assert.Equal("该设备未返回此项数据", rate.Note);
    }

    [Fact]
    public void DM5_离线且没读到时说明是离线而不是设备不配合()
    {
        var device = Device(PointerTelemetry.Empty, (DeviceCapability.RateRead, CapabilityState.Supported));
        device.IsOnline = false;

        Assert.Equal("设备离线，未读到该值", PointerTelemetryFormatter.DetailMetrics(device, Now)[0].Note);
    }

    [Fact]
    public void DM6_没声明能力的项整项不出现()
    {
        // 罗技 MX Master 3S：只有 DPI 有值，档位与回报率能力未声明 → 详情页只有一项
        var pointer = new PointerTelemetry
        {
            DpiX = TelemetryValue<int>.Reported(1000, T0),
            DpiY = TelemetryValue<int>.Reported(1000, T0),
        };
        var device = Device(pointer, (DeviceCapability.DpiRead, CapabilityState.Supported));

        var metric = Assert.Single(PointerTelemetryFormatter.DetailMetrics(device, Now));
        Assert.Equal(PointerMetricKind.Dpi, metric.Kind);
        Assert.Equal("1000", metric.Value);
    }
}
