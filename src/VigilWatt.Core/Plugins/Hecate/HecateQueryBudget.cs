namespace VigilWatt.Core.Plugins.Hecate;

/// <summary>
/// 功 能： HECATE 主动查询的每轮预算——一轮问几次、一次等多久（CR-100）。
/// 2026/10/29 21:40:00 dotisland
/// 关机是稳定状态而非瞬态，重试没有收益；依据与实测见 hecate.md §3。
/// </summary>
public static class HecateQueryBudget
{
    /// <summary>写查询帧的超时：与仓库其余 22 处写帧一致（500ms），不为本插件开特例。</summary>
    public static readonly TimeSpan WriteTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>听窗口：实测应答 &lt;1ms；500+150=650ms 守住基类半开探活单台 ≤700ms 的预算。</summary>
    public static readonly TimeSpan ListenWindow = TimeSpan.FromMilliseconds(150);

    /// <summary>两次尝试之间的间隔：只在过渡轮（问 2 次）时生效。</summary>
    public static readonly TimeSpan RetryGap = TimeSpan.FromMilliseconds(150);

    /// <summary>单次查询的保守上界——写超时与听窗口互斥（写失败即返回），真实最坏为两者取大。
    /// 用作 CR-100 的预算验收指标，单测锁定不超过基类的 700ms。</summary>
    public static TimeSpan WorstCaseQuery => WriteTimeout + ListenWindow;

    /// <summary>一轮问几次：探活/核实/已连续失败 → 1 次；其余（过渡轮）2 次滤单帧丢包。
    /// 核实轮须显式判——熔断会清零失败计数，只看计数会被当成首次失败而问 2 次（hecate.md §3）。</summary>
    public static int Attempts(bool halfOpenProbe, bool bypassActive, int consecutiveFailures)
        => halfOpenProbe || bypassActive || consecutiveFailures > 0 ? 1 : 2;
}
