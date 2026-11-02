using VigilWatt.Core.Plugins.Vgn;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： VgnBatteryDisplayLevel —— 电压换算显示电量，与官方驱动对齐。
/// 2026/11/02
///
/// 真机数据（2026-11-02 现场采集，见 scripts-reverse/probe_f2_sources.py 与 CDP 抓包）：
///   设备报 95%（payload[5]=0x5F）、电压 4076mV，官方驱动显示 94；
///   同一只鼠标电压 4072mV 时驱动显示 93。
/// 设备固件只输出 5% 一档（历史采样仅 75/80/85/90/95/100），
/// 所以 93/94 必然是驱动用电压插值算出来的。
/// </summary>
public sealed class VgnBatteryDisplayLevelTests
{
    /// <summary>现场两个锚点：这两条对上了，说明换算表和插值公式都是对的。</summary>
    [Theory]
    [InlineData(4072, 93)]   // 用户截图那一刻：设备报 90/95，驱动 93
    [InlineData(4076, 94)]   // 抓包那一刻：设备报 95，驱动 store 里是 94
    public void MatchesOfficialDriverOnRealSamples(int millivolts, int expected)
    {
        Assert.Equal(expected, VgnBatteryDisplayLevel.FromVoltage(millivolts, charging: false));
    }

    /// <summary>表里每个档位点本身就该落在 5% 的整数倍上（首档哨兵除外）。</summary>
    [Theory]
    [InlineData(3420, 5)]
    [InlineData(3480, 10)]
    [InlineData(3660, 25)]
    [InlineData(3840, 45)]
    [InlineData(4000, 75)]
    [InlineData(4060, 90)]
    [InlineData(4080, 95)]
    public void TableAnchorsMapToExpectedPercent(int millivolts, int expected)
    {
        Assert.Equal(expected, VgnBatteryDisplayLevel.FromVoltage(millivolts, charging: false));
    }

    /// <summary>超出表尾：充电中给 99、放电给 100 —— 驱动就是这么分的，别"修正"。</summary>
    [Theory]
    [InlineData(4200, true, 99)]
    [InlineData(4200, false, 100)]
    [InlineData(4111, true, 99)]
    [InlineData(4111, false, 100)]
    // 恰好等于表尾：驱动在这里返回 0%（满电显示成没电），是有意修正的边界
    [InlineData(4110, false, 100)]
    [InlineData(4110, true, 99)]
    public void AboveTableTopSplitsByCharging(int millivolts, bool charging, int expected)
    {
        Assert.Equal(expected, VgnBatteryDisplayLevel.FromVoltage(millivolts, charging));
    }

    /// <summary>低于最低档：0%。</summary>
    [Theory]
    [InlineData(3000)]
    [InlineData(1000)]
    [InlineData(1)]
    public void BelowBottomIsZero(int millivolts)
    {
        Assert.Equal(0, VgnBatteryDisplayLevel.FromVoltage(millivolts, charging: false));
    }

    /// <summary>电压为 0 = 设备没提供，调用方应退回设备百分比，这里必须返回 null 而不是 0。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MissingVoltageReturnsNull(int millivolts)
    {
        Assert.Null(VgnBatteryDisplayLevel.FromVoltage(millivolts, charging: false));
    }

    /// <summary>驱动特意避开 0 和 15 两个值（0 会被当成没读到），插值正好落在这两个数上时要 +1。</summary>
    [Fact]
    public void AvoidsExactlyZeroAndFifteen()
    {
        // 3540 按插值正好算出 15（驱动把 15 当低电量提示点，要避开）→ 应变成 16
        Assert.Equal(16, VgnBatteryDisplayLevel.FromVoltage(3540, charging: false));
        // 低于最低档时给 0，不走 +1（这里的 0 表示"确实没电"，与"读不到"无关）
        Assert.Equal(0, VgnBatteryDisplayLevel.FromVoltage(3000, charging: false));
    }

    /// <summary>
    /// 单调性：电压越高百分比不能越低。
    ///
    /// ⚠ 允许 1% 的逆向台阶：驱动有"算出来恰好 15 就 +1"的规则（15 是它的低电量提示档），
    /// 于是 3540mV→16% 而 3543mV→15% 会往回跳 1 格。这是原版行为带来的已知小瑕疵，
    /// 出现在 15% 附近（约 3540mV），为了和驱动显示一致，这里不复现修正、测试按 1% 容差。
    /// </summary>
    [Fact]
    public void IsMonotonicNonDecreasing()
    {
        int previous = -1;
        for (int mv = 3000; mv <= 4200; mv += 3)
        {
            int level = VgnBatteryDisplayLevel.FromVoltage(mv, charging: false)!.Value;
            Assert.True(level >= previous - 1, $"电压 {mv}mV 时 {level}% 比前一档 {previous}% 掉了超过 1 格");
            previous = Math.Max(previous, level);
        }
    }

    /// <summary>间隔不足一个 tick（10 秒）：保持旧值，不让设备电压抖动反映到界面。</summary>
    [Fact]
    public void SmoothHoldsInsideOneTick()
    {
        Assert.Equal(93, VgnBatteryDisplayLevel.Smooth(lastLevel: 93, factLevel: 99, seconds: 9));
        Assert.Equal(93, VgnBatteryDisplayLevel.Smooth(lastLevel: 93, factLevel: 50, seconds: 0));
    }

    /// <summary>间隔超过 30 分钟：直接采信新值。</summary>
    [Fact]
    public void SmoothTrustsFreshAfterHalfHour()
    {
        Assert.Equal(50, VgnBatteryDisplayLevel.Smooth(lastLevel: 93, factLevel: 50, seconds: 1801));
        Assert.Equal(99, VgnBatteryDisplayLevel.Smooth(lastLevel: 10, factLevel: 99, seconds: 7200));
    }

    /// <summary>
    /// 按 tick 走步：60 秒 = 6 步，差值在 6 以内直接到位。
    ///
    /// 这条是现场事故的回归——第一版把驱动"初始化时才用"的 0.014%/s 当成持续速率，
    /// 60 秒只降 0.84%，而驱动 60 秒能降 6%，导致放电时程序比驱动高 1~2 个点
    /// （用户实测：驱动 92、程序 93）。现在 94→92 这种 2 步的差必须在一次轮询内追平。
    /// </summary>
    [Theory]
    [InlineData(94, 92, 60, 92)]   // 现场：驱动 92、程序 93，目标是 92 → 必须到位
    [InlineData(93, 92, 60, 92)]
    [InlineData(95, 90, 60, 90)]   // 5 步 < 6 步额度，一次追平
    public void SmoothCatchesUpWithinOnePoll(int last, int fact, double seconds, int expected)
    {
        Assert.Equal(expected, VgnBatteryDisplayLevel.Smooth(last, fact, seconds));
    }

    /// <summary>差值超过本次 tick 数时按步数走，不一次跳到位（防止电压瞬断导致的暴跌）。</summary>
    [Fact]
    public void SmoothMovesAtMostOneStepPerTick()
    {
        // 60 秒 = 6 步，目标差 30 → 只走 6 步
        Assert.Equal(87, VgnBatteryDisplayLevel.Smooth(93, 63, 60));

        // 20 秒 = 2 步
        Assert.Equal(91, VgnBatteryDisplayLevel.Smooth(93, 63, 20));
    }

    /// <summary>上升方向同理：60 秒最多升 6 步。</summary>
    [Fact]
    public void SmoothRisesAtMostOneStepPerTick()
    {
        Assert.Equal(99, VgnBatteryDisplayLevel.Smooth(93, 100, 60));
        Assert.Equal(95, VgnBatteryDisplayLevel.Smooth(93, 100, 20));
    }
}
