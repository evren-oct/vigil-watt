namespace VigilWatt.Core.Plugins;

/// <summary>一次电量读数的判定结果。</summary>
public enum ReadingVerdict
{
    /// <summary>变化在合理范围内，正常采信。</summary>
    Accept,

    /// <summary>变化过大但已持续超过 <see cref="BatteryReadingFilter.PersistWindow"/>，认定为真实变化。</summary>
    AcceptConfirmed,

    /// <summary>变化过大，判为异常读数：本轮不采用（UI 与低电量判定都看不到这个值）。</summary>
    RejectAnomaly,

    /// <summary>数值本身无效（低于 <see cref="BatteryReadingFilter.MinValidPercent"/> 或 &gt;100），永不采信。</summary>
    InvalidValue,
}

/// <summary>
/// 功 能： 电量读数异常过滤器（每台设备一个实例）。
/// 2026/10/06 15:53:36 xliu
/// 阈值来源与调用链见 docs/project/code-flow.md §4.2。
/// </summary>
public sealed class BatteryReadingFilter
{
    /// <summary>允许的最大单次变化幅度（百分点，双向）。超过即判异常。</summary>
    public int MaxStepPercent { get; }

    /// <summary>异常读数需要持续多久才被采信为真实变化。</summary>
    public TimeSpan PersistWindow { get; }

    /// <summary>有效电量下界（含）。低于它的读数一律判无效——默认 2，即 0/1 都是休眠占位帧。</summary>
    public int MinValidPercent { get; }

    /// <summary>上次被采信的电量；-1 = 尚无基准（首帧直接采信）。</summary>
    public int LastAcceptedPercent { get; private set; } = -1;

    private DateTimeOffset? _anomalySince;

    public BatteryReadingFilter(int maxStepPercent, TimeSpan persistWindow, int minValidPercent = 2)
    {
        MaxStepPercent = Math.Max(1, maxStepPercent);
        PersistWindow = persistWindow > TimeSpan.Zero ? persistWindow : TimeSpan.FromMinutes(1);
        MinValidPercent = Math.Clamp(minValidPercent, 0, 100);
    }

    /// <summary>用探测阶段读到的值建立初始基准（不判定异常）。无效值当作"无基准"。</summary>
    public void Seed(int percent)
    {
        LastAcceptedPercent = IsValid(percent) ? percent : -1;
        _anomalySince = null;
    }

    /// <summary>判定一个读数是否可信；采信时自动把基准推进到该值。</summary>
    public ReadingVerdict Evaluate(int percent, DateTimeOffset now)
    {
        if (!IsValid(percent)) return ReadingVerdict.InvalidValue;

        if (LastAcceptedPercent < 0)
        {
            LastAcceptedPercent = percent;
            return ReadingVerdict.Accept;
        }

        if (Math.Abs(percent - LastAcceptedPercent) <= MaxStepPercent)
        {
            _anomalySince = null;          // 回到基准附近：持续计时清零
            LastAcceptedPercent = percent;
            return ReadingVerdict.Accept;
        }

        _anomalySince ??= now;             // 异常窗口起点（连续异常不重置）
        if (now - _anomalySince.Value < PersistWindow)
            return ReadingVerdict.RejectAnomaly;

        _anomalySince = null;
        LastAcceptedPercent = percent;
        return ReadingVerdict.AcceptConfirmed;
    }

    /// <summary>
    /// 离线后重新上线的那一拍：丢掉旧基准与异常计时，直接以新读数重建。
    /// 离线期间电量可以大幅变化（充电、换电池、占位帧），拿旧基准比对会被"持续 5 分钟"挡住（CR-124）。
    /// 无效值仍判 <see cref="ReadingVerdict.InvalidValue"/>，0/1 这类休眠占位帧不会被当基准。
    /// </summary>
    public ReadingVerdict Reseed(int percent)
    {
        if (!IsValid(percent)) return ReadingVerdict.InvalidValue;
        LastAcceptedPercent = percent;
        _anomalySince = null;
        return ReadingVerdict.Accept;
    }

    private bool IsValid(int percent) => percent >= MinValidPercent && percent <= 100;
}
