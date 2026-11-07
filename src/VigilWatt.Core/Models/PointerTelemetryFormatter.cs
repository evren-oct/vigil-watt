using System.Globalization;

namespace VigilWatt.Core.Models;

/// <summary>
/// 功 能： 卡片上三个指标的类别；UI 按它取标签色，不靠解析标签文字。
/// 2026/11/07 18:50:00 xliu
/// </summary>
public enum PointerMetricKind
{
    Dpi = 0,
    Level,
    Rate,
}

/// <summary>
/// 功 能： 一个指标项（标签 + 值 + 是否已获取）。Value 已含单位与来源标记，UI 不再拼接。
/// 2026/11/07 18:50:00 xliu
/// IsKnown=false 即"未获取"，调用方据此灰显且不上色（docs/project/pointer-telemetry.md §8）。
/// </summary>
public readonly record struct PointerMetric(PointerMetricKind Kind, string Label, string Value, bool IsKnown);

/// <summary>
/// 功 能： 指针遥测在卡片上的分段与单行文案，口径见 docs/project/pointer-telemetry.md §8。
/// 2026/11/07 18:40:00 xliu
/// 纯函数零 UI：设备页只绑定 <see cref="Metrics"/>，三态判定由 PointerTelemetryFormatterTests 钉住（PR-D）。
/// </summary>
public static class PointerTelemetryFormatter
{
    /// <summary>统一未知文案。缺读数与已过期都落在这里，不另造"已过期"字样。</summary>
    public const string UnknownText = "未获取";

    private const string Sep = " · ";

    /// <summary>按 DPI → 档位 → 回报率 顺序返回要渲染的指标；离线或无任何可展示项时返回空表。</summary>
    public static IReadOnlyList<PointerMetric> Metrics(DeviceStatus device, DateTimeOffset now)
    {
        if (!device.IsOnline) return Array.Empty<PointerMetric>();

        var metrics = new List<PointerMetric>(3);
        Add(metrics, Dpi(device, now));
        Add(metrics, Level(device, now));
        Add(metrics, Rate(device, now));
        return metrics;
    }

    /// <summary>单行摘要（签名、日志与测试用）；null = 整行不渲染。不为刷新一行 UI 发查询帧，见 §7 约束 2。</summary>
    public static string? Summary(DeviceStatus device, DateTimeOffset now)
    {
        var metrics = Metrics(device, now);
        return metrics.Count == 0
            ? null
            : string.Join(Sep, metrics.Select(m => $"{m.Label} {m.Value}"));
    }

    /// <summary>两轴一致只写一个数；不一致写 "1600×1200"，不拿任一轴代表两轴。</summary>
    private static PointerMetric? Dpi(DeviceStatus device, DateTimeOffset now)
    {
        var pointer = device.Pointer;
        bool hasX = Fresh(pointer.DpiX, now);
        bool hasY = Fresh(pointer.DpiY, now);

        if (hasX || hasY)
        {
            string value = hasX && hasY
                ? (pointer.DpiX.Value == pointer.DpiY.Value
                    ? Num(pointer.DpiX)
                    : $"{Num(pointer.DpiX)}×{Num(pointer.DpiY)}")
                : Num(hasX ? pointer.DpiX : pointer.DpiY);
            var source = (hasX ? pointer.DpiX : pointer.DpiY).Source;
            return new PointerMetric(PointerMetricKind.Dpi, "DPI", $"{value}{SourceSuffix(source)}", true);
        }

        return Supported(device, DeviceCapability.DpiRead)
            ? new PointerMetric(PointerMetricKind.Dpi, "DPI", UnknownText, false)
            : null;
    }

    /// <summary>档位索引原样输出，不加"第"：0 基还是 1 基由厂商协议决定，Core 不设上下界也不猜。</summary>
    private static PointerMetric? Level(DeviceStatus device, DateTimeOffset now)
    {
        var pointer = device.Pointer;

        if (Fresh(pointer.DpiLevel, now))
        {
            string total = Fresh(pointer.DpiLevelCount, now) ? $"/{Num(pointer.DpiLevelCount)}" : "";
            string value = $"{Num(pointer.DpiLevel)}{total}{SourceSuffix(pointer.DpiLevel.Source)}";
            return new PointerMetric(PointerMetricKind.Level, "档位", value, true);
        }

        return Supported(device, DeviceCapability.DpiLevelRead)
            ? new PointerMetric(PointerMetricKind.Level, "档位", UnknownText, false)
            : null;
    }

    /// <summary>设置值优先；实测值只在没有设置值时出现，且带 ≈ 与来源标记，绝不冒充设备配置。</summary>
    private static PointerMetric? Rate(DeviceStatus device, DateTimeOffset now)
    {
        var pointer = device.Pointer;

        if (Fresh(pointer.ReportRateSettingHz, now))
        {
            string value = $"{Num(pointer.ReportRateSettingHz)} Hz{SourceSuffix(pointer.ReportRateSettingHz.Source)}";
            return new PointerMetric(PointerMetricKind.Rate, "回报率", value, true);
        }

        if (Fresh(pointer.ReportRateMeasuredHz, now))
        {
            string value = $"≈{Num(pointer.ReportRateMeasuredHz)} Hz{SourceSuffix(pointer.ReportRateMeasuredHz.Source)}";
            return new PointerMetric(PointerMetricKind.Rate, "回报率", value, true);
        }

        return Supported(device, DeviceCapability.RateRead)
            ? new PointerMetric(PointerMetricKind.Rate, "回报率", UnknownText, false)
            : null;
    }

    /// <summary>只有非设备直报的来源才挂标记：DeviceReported 是默认态，全挂标记会把卡片塞满。</summary>
    private static string SourceSuffix(TelemetrySource source) => source switch
    {
        TelemetrySource.Estimated => "（估算）",
        TelemetrySource.Inferred => "（推断）",
        TelemetrySource.VendorSdk => "（厂商）",
        _ => "",
    };

    /// <summary>有值且没过期才算"当前值"；过期后与从未读到同样处理。</summary>
    private static bool Fresh(TelemetryValue<int> value, DateTimeOffset now)
        => value.HasValue && !value.IsStale(now, PointerTelemetry.DefaultMaxAge);

    /// <summary>只有声明为 supported 才敢说"未获取"：unverified / unsupported / 未声明都无从判断是否支持。</summary>
    private static bool Supported(DeviceStatus device, DeviceCapability capability)
        => device.Capabilities.Of(capability) == CapabilityState.Supported;

    private static string Num(TelemetryValue<int> value)
        => value.Value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture);

    private static void Add(List<PointerMetric> metrics, PointerMetric? metric)
    {
        if (metric is { } value) metrics.Add(value);
    }
}
