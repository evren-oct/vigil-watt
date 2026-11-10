namespace VigilWatt.Core.Models;

/// <summary>
/// 功 能： 遥测值的**信息来源**（谁告诉我们的），不是"数值经过了几次转换"。
/// 2026/11/03 22:10:00 xliu
/// Unknown 表示没有可靠途径，不等于"值等于 0"；判定口径见 docs/project/pointer-telemetry.md §2。
/// </summary>
public enum TelemetrySource
{
    /// <summary>没有可靠途径。显式 0：<c>default(TelemetryValue&lt;T&gt;)</c> 必须落在这里。</summary>
    Unknown = 0,

    /// <summary>设备在协议帧里给出，且解码只用到协议已定义、本项目已验证的编码（字节序 / 位域 / 码表）。</summary>
    DeviceReported,

    /// <summary>厂商 SDK / 官方服务返回（当前无实现）。</summary>
    VendorSdk,

    /// <summary>需要设备未确认过的假设才能得到该量（型号映射表未验证、"配置值等于生效值"之类）。</summary>
    Inferred,

    /// <summary>系统事件统计（只允许用在实测回报率上）。</summary>
    Estimated,
}

/// <summary>
/// 功 能： 一个带来源与观测时刻的只读遥测值；Unknown 与 0 严格区分。
/// 2026/11/03 22:10:00 xliu
/// 值类型：Clone 只需赋值，不会与数据源共享引用。
/// </summary>
public readonly record struct TelemetryValue<T> where T : struct
{
    private TelemetryValue(T? value, TelemetrySource source, DateTimeOffset observedAt)
    {
        Value = value;
        Source = source;
        ObservedAt = observedAt;
    }

    public T? Value { get; }

    public TelemetrySource Source { get; }

    /// <summary>最近一次观测时刻。<c>default</c> = 时间未知（兜底语义，不是正常解析结果）。</summary>
    public DateTimeOffset ObservedAt { get; }

    /// <summary>有可信值。Source 为 Unknown 时即使 Value 有值也判无值——堵住"拿 0 冒充未知"。</summary>
    public bool HasValue => Value.HasValue && Source != TelemetrySource.Unknown;

    public static TelemetryValue<T> Unknown => default;

    public static TelemetryValue<T> Reported(T value, DateTimeOffset observedAt)
        => new(value, TelemetrySource.DeviceReported, observedAt);

    public static TelemetryValue<T> Inferred(T value, DateTimeOffset observedAt)
        => new(value, TelemetrySource.Inferred, observedAt);

    public static TelemetryValue<T> Estimated(T value, DateTimeOffset observedAt)
        => new(value, TelemetrySource.Estimated, observedAt);

    public static TelemetryValue<T> FromVendorSdk(T value, DateTimeOffset observedAt)
        => new(value, TelemetrySource.VendorSdk, observedAt);

    /// <summary>incoming 有值则整体替换（值 + 来源 + 时刻），否则保留 stored。放这里才能用私有构造。</summary>
    public static TelemetryValue<T> Merge(TelemetryValue<T> stored, TelemetryValue<T> incoming)
        => incoming.HasValue ? incoming : stored;

    /// <summary>
    /// 按来源可信度合并：高可信可覆盖低可信，反向不覆盖；同来源按"新观测胜"。与 <see cref="Merge"/> 的区别是它不看先后、只看证据强度。
    /// </summary>
    public static TelemetryValue<T> MergePreferTrusted(TelemetryValue<T> stored, TelemetryValue<T> incoming)
    {
        if (!incoming.HasValue) return stored;
        if (!stored.HasValue) return incoming;
        return Trust(incoming.Source) >= Trust(stored.Source) ? incoming : stored;
    }

    /// <summary>可信度排序：设备上报 &gt; 厂商 SDK &gt; 推断 &gt; 估算。Unknown 由 HasValue 挡在门外，这里给最低值只为完备。</summary>
    private static int Trust(TelemetrySource source) => source switch
    {
        TelemetrySource.DeviceReported => 4,
        TelemetrySource.VendorSdk => 3,
        TelemetrySource.Inferred => 2,
        TelemetrySource.Estimated => 1,
        _ => 0,
    };

    /// <summary>换一个观测时刻（值与来源不变）。解析层不带时钟，由调用方在边界处统一盖时间。</summary>
    public TelemetryValue<T> WithObservedAt(DateTimeOffset observedAt)
        => HasValue ? new(Value, Source, observedAt) : this;

    /// <summary>无值 / 时间未知 / 时间在未来一律 false；maxAge 为负抛 ArgumentOutOfRangeException。</summary>
    public bool IsStale(DateTimeOffset now, TimeSpan maxAge)
    {
        if (maxAge < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxAge));
        if (!HasValue || ObservedAt == default) return false;
        return now - ObservedAt > maxAge;
    }
}

/// <summary>
/// 功 能： 指针类遥测（DPI / 回报率）的只读快照；默认值 = 全部未知。
/// 2026/11/03 22:10:00 xliu
/// "设置值"与"实测值"是两个字段，命名上不许混；语义见 docs/project/pointer-telemetry.md §3。
/// </summary>
public readonly record struct PointerTelemetry
{
    /// <summary>应用层默认过期阈值。这是本应用的展示策略，**不是协议/设备规则**；各厂商接入时需单独核对。</summary>
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromMinutes(5);

    public TelemetryValue<int> DpiX { get; init; }

    public TelemetryValue<int> DpiY { get; init; }

    /// <summary>档位索引，不是 DPI 值；范围由厂商协议决定，Core 不设上下界。</summary>
    public TelemetryValue<int> DpiLevel { get; init; }

    public TelemetryValue<int> DpiLevelCount { get; init; }

    /// <summary>设备保存的回报率设置值（不是实际吞吐）。</summary>
    public TelemetryValue<int> ReportRateSettingHz { get; init; }

    /// <summary>系统估算的实测回报率；只允许 <see cref="TelemetrySource.Estimated"/> 来源。</summary>
    public TelemetryValue<int> ReportRateMeasuredHz { get; init; }

    public static PointerTelemetry Empty => default;

    /// <summary>给"有值但时间未知"的项盖上观测时刻；已有时间的项原样保留（幂等）。</summary>
    public PointerTelemetry WithObservedAt(DateTimeOffset observedAt)
    {
        if (!HasAny) return this;
        return new PointerTelemetry
        {
            DpiX = Stamp(DpiX, observedAt),
            DpiY = Stamp(DpiY, observedAt),
            DpiLevel = Stamp(DpiLevel, observedAt),
            DpiLevelCount = Stamp(DpiLevelCount, observedAt),
            ReportRateSettingHz = Stamp(ReportRateSettingHz, observedAt),
            ReportRateMeasuredHz = Stamp(ReportRateMeasuredHz, observedAt),
        };
    }

    private static TelemetryValue<int> Stamp(TelemetryValue<int> value, DateTimeOffset observedAt)
        => value.HasValue && value.ObservedAt == default ? value.WithObservedAt(observedAt) : value;

    /// <summary>任意一项有可信值。</summary>
    public bool HasAny => DpiX.HasValue || DpiY.HasValue || DpiLevel.HasValue
                          || DpiLevelCount.HasValue || ReportRateSettingHz.HasValue
                          || ReportRateMeasuredHz.HasValue;

    /// <summary>有值项里最新的观测时刻；全空返回 null。</summary>
    public DateTimeOffset? LatestObservedAt
    {
        get
        {
            DateTimeOffset? latest = null;
            Track(DpiX);
            Track(DpiY);
            Track(DpiLevel);
            Track(DpiLevelCount);
            Track(ReportRateSettingHz);
            Track(ReportRateMeasuredHz);
            return latest;

            void Track(TelemetryValue<int> value)
            {
                if (!value.HasValue) return;
                if (latest is null || value.ObservedAt > latest.Value) latest = value.ObservedAt;
            }
        }
    }

    public bool IsStale(DateTimeOffset now) => IsStale(now, DefaultMaxAge);

    /// <summary>所有有值项都过期才算整块过期；全空返回 false（没有值 ≠ 过期）。</summary>
    public bool IsStale(DateTimeOffset now, TimeSpan maxAge)
    {
        if (maxAge < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxAge));
        if (!HasAny) return false;
        return (!DpiX.HasValue || DpiX.IsStale(now, maxAge))
               && (!DpiY.HasValue || DpiY.IsStale(now, maxAge))
               && (!DpiLevel.HasValue || DpiLevel.IsStale(now, maxAge))
               && (!DpiLevelCount.HasValue || DpiLevelCount.IsStale(now, maxAge))
               && (!ReportRateSettingHz.HasValue || ReportRateSettingHz.IsStale(now, maxAge))
               && (!ReportRateMeasuredHz.HasValue || ReportRateMeasuredHz.IsStale(now, maxAge));
    }

    /// <summary>逐字段合并：incoming 无值则保留 stored，有值则连来源与时刻一起覆盖（不比时间戳）。</summary>
    public static PointerTelemetry Merge(PointerTelemetry stored, PointerTelemetry incoming) => new()
    {
        DpiX = TelemetryValue<int>.Merge(stored.DpiX, incoming.DpiX),
        DpiY = TelemetryValue<int>.Merge(stored.DpiY, incoming.DpiY),
        DpiLevel = TelemetryValue<int>.Merge(stored.DpiLevel, incoming.DpiLevel),
        DpiLevelCount = TelemetryValue<int>.Merge(stored.DpiLevelCount, incoming.DpiLevelCount),
        ReportRateSettingHz = TelemetryValue<int>.Merge(stored.ReportRateSettingHz, incoming.ReportRateSettingHz),
        ReportRateMeasuredHz = TelemetryValue<int>.Merge(stored.ReportRateMeasuredHz, incoming.ReportRateMeasuredHz),
    };

    /// <summary>incoming 是否带来与 stored 不同的可信值：只比 Value + Source，不比 ObservedAt。</summary>
    public static bool ValueDiffers(PointerTelemetry stored, PointerTelemetry incoming)
        => Differs(stored.DpiX, incoming.DpiX)
           || Differs(stored.DpiY, incoming.DpiY)
           || Differs(stored.DpiLevel, incoming.DpiLevel)
           || Differs(stored.DpiLevelCount, incoming.DpiLevelCount)
           || Differs(stored.ReportRateSettingHz, incoming.ReportRateSettingHz)
           || Differs(stored.ReportRateMeasuredHz, incoming.ReportRateMeasuredHz);

    /// <summary>incoming 无值 = 没带信息，不算差异。</summary>
    private static bool Differs(TelemetryValue<int> stored, TelemetryValue<int> incoming)
        => incoming.HasValue
           && (!stored.HasValue
               || stored.Value.GetValueOrDefault() != incoming.Value.GetValueOrDefault()
               || stored.Source != incoming.Source);
}
