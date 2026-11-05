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

    /// <summary>小幅来回抖动（幅度内但方向反复），未达连续同向次数：本轮不采用。</summary>
    RejectJitter,

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

    /// <summary>小幅变化需要连续同向多少次才采信（抗 1~2 点的来回抖动）。1 = 关闭去抖。</summary>
    public int JitterConfirmCount { get; }

    /// <summary>上次被采信的电量；-1 = 尚无基准（首帧直接采信）。</summary>
    public int LastAcceptedPercent { get; private set; } = -1;

    private DateTimeOffset? _anomalySince;
    private int _pendingDelta;
    private int _pendingCount;

    public BatteryReadingFilter(int maxStepPercent, TimeSpan persistWindow, int minValidPercent = 2,
        int jitterConfirmCount = 3)
    {
        MaxStepPercent = Math.Max(1, maxStepPercent);
        PersistWindow = persistWindow > TimeSpan.Zero ? persistWindow : TimeSpan.FromMinutes(1);
        MinValidPercent = Math.Clamp(minValidPercent, 0, 100);
        JitterConfirmCount = Math.Max(1, jitterConfirmCount);
    }

    /// <summary>用探测阶段读到的值建立初始基准（不判定异常）。无效值当作"无基准"。</summary>
    public void Seed(int percent)
    {
        LastAcceptedPercent = IsValid(percent) ? percent : -1;
        _anomalySince = null;
        ResetJitter();
    }

    /// <summary>判定一个读数是否可信；采信时自动把基准推进到该值。</summary>
    public ReadingVerdict Evaluate(int percent, DateTimeOffset now)
    {
        if (!IsValid(percent)) return ReadingVerdict.InvalidValue;

        if (LastAcceptedPercent < 0)
        {
            LastAcceptedPercent = percent;
            ResetJitter();
            return ReadingVerdict.Accept;
        }

        int delta = percent - LastAcceptedPercent;
        if (Math.Abs(delta) <= MaxStepPercent)
        {
            _anomalySince = null;          // 回到基准附近：持续计时清零
            if (ConfirmJitter(delta)) return ReadingVerdict.RejectJitter;

            LastAcceptedPercent = percent;
            ResetJitter();
            return ReadingVerdict.Accept;
        }

        ResetJitter();
        _anomalySince ??= now;             // 异常窗口起点（连续异常不重置）
        if (now - _anomalySince.Value < PersistWindow)
            return ReadingVerdict.RejectAnomaly;

        _anomalySince = null;
        LastAcceptedPercent = percent;
        return ReadingVerdict.AcceptConfirmed;
    }

    /// <summary>
    /// 小幅变化要连续同向 <see cref="JitterConfirmCount"/> 次才采信；方向一换就重新计数。
    /// 两个相邻的**不同**读数已经算 2 个证据（基础值本身 + 新值），所以还差 <c>Count-1</c> 次。
    /// <b>读数正好等于基准（delta=0）时既不推进也不清零</b>：80→81→80→81 这种锯齿若在这里清零，
    /// 计数会被每一拍重置、永远凑不满，等于去抖失效（2026-11-05 实测踩到）。
    /// </summary>
    private bool ConfirmJitter(int delta)
    {
        if (JitterConfirmCount <= 1) { ResetJitter(); return false; }
        if (delta == 0) return false;

        if (Math.Sign(delta) == Math.Sign(_pendingDelta) && _pendingDelta != 0) _pendingCount++;
        else { _pendingDelta = delta; _pendingCount = 1; }

        return _pendingCount < JitterConfirmCount - 1;
    }

    private void ResetJitter()
    {
        _pendingDelta = 0;
        _pendingCount = 0;
    }

    private bool IsValid(int percent) => percent >= MinValidPercent && percent <= 100;
}
