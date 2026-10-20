using VigilWatt.Core.Plugins;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： BatteryReadingFilter —— 读数异常过滤：数值有效性（休眠占位帧 0/1）+ 双向变化幅度。
/// 2026/10/06 15:53:36 xliu
/// 用例覆盖正常放电、休眠伪读数、非占位骤降/骤升、持续超窗采信、临界幅度等。
/// </summary>
public class BatteryReadingFilterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 12, 0, 0, TimeSpan.FromHours(8));

    private static BatteryReadingFilter NewFilter()
        => new(maxStepPercent: 20, persistWindow: TimeSpan.FromMinutes(5));

    [Fact]
    public void ProtocolWithExplicitDisconnectSentinelAllowsRealZeroAndOnePercent()
    {
        var filter = new BatteryReadingFilter(20, TimeSpan.FromMinutes(5), minValidPercent: 0);
        filter.Seed(2);
        Assert.Equal(ReadingVerdict.Accept, filter.Evaluate(1, T0));
        Assert.Equal(ReadingVerdict.Accept, filter.Evaluate(0, T0.AddMinutes(1)));
        Assert.Equal(ReadingVerdict.InvalidValue, filter.Evaluate(-1, T0.AddMinutes(2)));
        Assert.Equal(ReadingVerdict.InvalidValue, filter.Evaluate(101, T0.AddMinutes(3)));
    }

    [Fact]
    public void A1_正常放电_小步下降逐步采信()
    {
        var f = NewFilter();
        f.Seed(66);
        Assert.Equal(ReadingVerdict.Accept, f.Evaluate(65, T0.AddMinutes(1)));
        Assert.Equal(ReadingVerdict.Accept, f.Evaluate(64, T0.AddMinutes(2)));
        Assert.Equal(64, f.LastAcceptedPercent); // 基准已推进到 64
    }

    [Fact]
    public void A2_休眠占位帧_报1或0永不采信()
    {
        var f = NewFilter();
        f.Seed(66);
        Assert.Equal(ReadingVerdict.InvalidValue, f.Evaluate(1, T0.AddMinutes(1)));
        Assert.Equal(ReadingVerdict.InvalidValue, f.Evaluate(0, T0.AddMinutes(2)));
        Assert.Equal(ReadingVerdict.InvalidValue, f.Evaluate(1, T0.AddMinutes(60))); // 隔很久仍不采信
        Assert.Equal(66, f.LastAcceptedPercent);                                      // 基准未被污染
    }

    [Fact]
    public void A3_非占位异常骤降_抖动过不去()
    {
        var f = NewFilter();
        f.Seed(66);
        Assert.Equal(ReadingVerdict.RejectAnomaly, f.Evaluate(5, T0.AddMinutes(1)));
        Assert.Equal(ReadingVerdict.Accept, f.Evaluate(66, T0.AddMinutes(2)));        // 回弹 → 计时清零
        Assert.Equal(ReadingVerdict.RejectAnomaly, f.Evaluate(5, T0.AddMinutes(3)));  // 计时重新开始
        Assert.Equal(ReadingVerdict.RejectAnomaly, f.Evaluate(5, T0.AddMinutes(6)));  // 未到 5 分钟
        Assert.Equal(66, f.LastAcceptedPercent);
    }

    [Fact]
    public void A4_异常值持续超过窗口_最终采信()
    {
        var f = NewFilter();
        f.Seed(66);
        Assert.Equal(ReadingVerdict.RejectAnomaly, f.Evaluate(10, T0));
        Assert.Equal(ReadingVerdict.RejectAnomaly, f.Evaluate(10, T0.AddMinutes(4)));
        Assert.Equal(ReadingVerdict.AcceptConfirmed, f.Evaluate(10, T0.AddMinutes(5))); // 到窗口 → 采信
        Assert.Equal(10, f.LastAcceptedPercent);
    }

    [Fact]
    public void A5_反向异常_骤升同样过滤()
    {
        var f = NewFilter();
        f.Seed(66);
        Assert.Equal(ReadingVerdict.RejectAnomaly, f.Evaluate(99, T0.AddMinutes(1)));
    }

    [Fact]
    public void A6_越界值永不采信()
    {
        var f = NewFilter();
        f.Seed(66);
        Assert.Equal(ReadingVerdict.InvalidValue, f.Evaluate(101, T0.AddMinutes(1)));
        Assert.Equal(ReadingVerdict.InvalidValue, f.Evaluate(-3, T0.AddMinutes(2)));
    }

    [Fact]
    public void A7_无基准时首帧行为()
    {
        var f = NewFilter();
        Assert.Equal(ReadingVerdict.Accept, f.Evaluate(42, T0)); // 首帧直接采信

        var g = NewFilter();
        Assert.Equal(ReadingVerdict.InvalidValue, g.Evaluate(1, T0)); // 首帧 1% 也不采信
    }

    [Fact]
    public void A8_临界值_正好20点正常_21点异常()
    {
        var f = NewFilter();
        f.Seed(66);
        Assert.Equal(ReadingVerdict.Accept, f.Evaluate(46, T0.AddMinutes(1))); // 差 20 → 正常

        var g = NewFilter();
        g.Seed(66);
        Assert.Equal(ReadingVerdict.RejectAnomaly, g.Evaluate(45, T0.AddMinutes(1))); // 差 21 → 异常
    }

    [Fact]
    public void A9_上涨方向_小步采信_大步先过滤()
    {
        var f = NewFilter();
        f.Seed(60);
        Assert.Equal(ReadingVerdict.Accept, f.Evaluate(75, T0.AddMinutes(1))); // 差 15 → 直接采信

        var g = NewFilter();
        g.Seed(40);
        Assert.Equal(ReadingVerdict.RejectAnomaly, g.Evaluate(75, T0.AddMinutes(1)));  // 差 35 → 先过滤
        Assert.Equal(ReadingVerdict.AcceptConfirmed, g.Evaluate(75, T0.AddMinutes(6))); // 持续 5 分钟 → 采信
    }
}
