using VigilWatt.Core.Storage;

namespace VigilWatt.Core.Devices;

public enum RemainingTimeUnavailableReason
{
    None,
    Disabled,
    Offline,
    Charging,
    Full,
    MissingReading,
    InvalidReading,
    CollectingSamples,
    CollectingAfterCharge,
    WaitingForBatteryDrop,
    InvalidHistory,
}

public readonly record struct RemainingTimeEstimateResult(
    TimeSpan? Remaining,
    RemainingTimeUnavailableReason Reason)
{
    public bool IsAvailable => Remaining.HasValue && Reason == RemainingTimeUnavailableReason.None;

    public string Explain() => Reason switch
    {
        RemainingTimeUnavailableReason.Disabled => "剩余时间估算已关闭",
        RemainingTimeUnavailableReason.Offline => "设备离线，暂不可估算",
        RemainingTimeUnavailableReason.Charging => "充电中，暂停估算",
        RemainingTimeUnavailableReason.Full => "设备已充满，放电后开始估算",
        RemainingTimeUnavailableReason.MissingReading => "暂无电量读数",
        RemainingTimeUnavailableReason.InvalidReading => "当前电量无效，暂不可估算",
        RemainingTimeUnavailableReason.CollectingSamples => "采集点不足，至少需要 3 个不同时间点",
        RemainingTimeUnavailableReason.CollectingAfterCharge => "充电后采集点不足，至少需要 3 个不同时间点",
        RemainingTimeUnavailableReason.WaitingForBatteryDrop => "近期电量尚无净下降",
        RemainingTimeUnavailableReason.InvalidHistory => "历史数据不足以计算放电速度",
        _ => "",
    };
}

/// <summary>
/// 功 能： 根据近期历史电量估算设备剩余使用时间。
/// 2026/10/18 16:02:29 xliu
/// </summary>
public static class RemainingTimeEstimator
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(90);
    public const int MinimumSamples = 3;

    public static TimeSpan? Estimate(IReadOnlyList<HistorySample> samples, int currentPercent, DateTimeOffset now)
        => Evaluate(samples, currentPercent, now).Remaining;

    public static RemainingTimeEstimateResult Evaluate(
        IReadOnlyList<HistorySample> samples, int currentPercent, DateTimeOffset now)
    {
        var inWindow = samples
            .Where(s => s.Timestamp >= now - Window && s.Timestamp <= now)
            .OrderBy(s => s.Timestamp)
            .ThenBy(s => s.Id)
            .GroupBy(s => s.Timestamp)
            .Select(g => g.Last())
            .ToList();
        if (inWindow.Count < MinimumSamples)
            return new(null, RemainingTimeUnavailableReason.CollectingSamples);

        int lastCharge = inWindow.FindLastIndex(s => s.Charging);
        var discharge = inWindow.Skip(lastCharge + 1).Where(s => !s.Charging).ToList();
        if (discharge.Count < MinimumSamples)
            return new(null, lastCharge >= 0
                ? RemainingTimeUnavailableReason.CollectingAfterCharge
                : RemainingTimeUnavailableReason.CollectingSamples);

        var first = discharge[0];
        var last = discharge[^1];
        var elapsed = last.Timestamp - first.Timestamp;
        int drop = first.Percent - last.Percent;
        if (currentPercent <= 0 || currentPercent > 100)
            return new(null, RemainingTimeUnavailableReason.InvalidReading);
        if (elapsed <= TimeSpan.Zero)
            return new(null, RemainingTimeUnavailableReason.InvalidHistory);
        if (drop <= 0)
            return new(null, RemainingTimeUnavailableReason.WaitingForBatteryDrop);

        double pointsPerHour = drop / elapsed.TotalHours;
        if (!double.IsFinite(pointsPerHour) || pointsPerHour <= 0)
            return new(null, RemainingTimeUnavailableReason.InvalidHistory);

        double hours = currentPercent / pointsPerHour;
        if (!double.IsFinite(hours) || hours <= 0 || hours > TimeSpan.MaxValue.TotalHours)
            return new(null, RemainingTimeUnavailableReason.InvalidHistory);
        return new(TimeSpan.FromHours(hours), RemainingTimeUnavailableReason.None);
    }

    public static string Format(TimeSpan remaining)
    {
        var totalMinutes = Math.Max(1, (int)Math.Round(remaining.TotalMinutes));
        int days = totalMinutes / (24 * 60);
        int hours = totalMinutes % (24 * 60) / 60;
        int minutes = totalMinutes % 60;
        if (days > 0) return hours > 0 ? $"{days}天{hours}小时" : $"{days}天";
        if (hours > 0) return minutes > 0 ? $"{hours}小时{minutes}分钟" : $"{hours}小时";
        return $"{minutes}分钟";
    }

    /// <summary>
    /// 带总小时数的写法：「2天6小时（54小时）」。天数不好心算成小时，补一个总数给要汇算的场合。
    /// 2026/11/11 22:25:00 20211
    /// 不足一天不补括号（本来就是小时）；总数与 <see cref="Format"/> 同源，两个数字才对得上。
    /// </summary>
    public static string FormatWithTotalHours(TimeSpan remaining)
    {
        string text = Format(remaining);
        int totalMinutes = Math.Max(1, (int)Math.Round(remaining.TotalMinutes));
        // Format 在天级已经截掉分钟，这里也按同一份分钟数取整，避免出现"2天6小时（55小时）"
        return totalMinutes < 24 * 60 ? text : $"{text}（{totalMinutes / 60}小时）";
    }
}
