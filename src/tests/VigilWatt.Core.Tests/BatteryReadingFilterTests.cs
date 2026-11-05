using VigilWatt.Core.Plugins;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： BatteryReadingFilter —— 读数异常过滤：数值有效性（休眠占位帧 0/1）+ 双向变化幅度 + 小幅抖动去抖。
/// 2026/10/06 15:53:36 xliu
/// 用例覆盖正常放电、休眠伪读数、非占位骤降/骤升、持续超窗采信、临界幅度、1~2 点来回抖动。
/// </summary>
public class BatteryReadingFilterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 12, 0, 0, TimeSpan.FromHours(8));

    private static BatteryReadingFilter NewFilter()
        => new(maxStepPercent: 20, persistWindow: TimeSpan.FromMinutes(5));

    /// <summary>小幅变化：第 1 次按住，第 2 次同向采信（基准值与新值本身就是 2 个证据）。</summary>
    [Fact]
    public void A1_正常放电_连续同向下行_两次后采信()
    {
        var f = NewFilter();
        f.Seed(66);
        Assert.Equal(ReadingVerdict.RejectJitter, f.Evaluate(65, T0.AddMinutes(1)));
        Assert.Equal(66, f.LastAcceptedPercent);
        Assert.Equal(ReadingVerdict.Accept, f.Evaluate(64, T0.AddMinutes(2)));
        Assert.Equal(64, f.LastAcceptedPercent);
    }

    /// <summary>单次掉一格后稳住了：读数回到"新的稳定值"时同样采信，不会被永久挡住。</summary>
    [Fact]
    public void A1b_单次掉一格稳住后_下一拍采信()
    {
        var f = NewFilter();
        f.Seed(66);
        Assert.Equal(ReadingVerdict.RejectJitter, f.Evaluate(65, T0.AddMinutes(1)));
        Assert.Equal(ReadingVerdict.Accept, f.Evaluate(65, T0.AddMinutes(2)));
        Assert.Equal(65, f.LastAcceptedPercent);
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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ProtocolCanOptInToRealLowBatteryValues(int percent)
    {
        // 低电量档位：**真正零写入的被动协议**才用它。此类协议没有查询可重试，
        // 去抖只会把真实掉电多拖两轮，故按协议关掉（jitterConfirmCount: 1）。
        var filter = new BatteryReadingFilter(20, TimeSpan.FromMinutes(5), minValidPercent: 0,
            jitterConfirmCount: 1);
        filter.Seed(2);
        Assert.Equal(ReadingVerdict.Accept, filter.Evaluate(percent, T0));
        Assert.Equal(percent, filter.LastAcceptedPercent);
    }

    /// <summary>去抖默认开启时，2%→0%/1% 会被当作小幅变化按住——这是有意行为，由协议方决定是否关。</summary>
    [Fact]
    public void J6_默认去抖下低电量档位也需要确认()
    {
        var f = new BatteryReadingFilter(20, TimeSpan.FromMinutes(5), minValidPercent: 0);
        f.Seed(2);
        Assert.Equal(ReadingVerdict.RejectJitter, f.Evaluate(1, T0));
        Assert.Equal(ReadingVerdict.Accept, f.Evaluate(1, T0.AddMinutes(1)));
    }

    /// <summary>
    /// 蝰蛇 X11 实录：接近满电时设备读数在 80/81/82 之间来回跳（2026-11-05 用户复报"电量越用越多"）。
    /// 期望：**偏离基准的读数一律按住**（RejectJitter）；读数正好等于基准时判 Accept 但基准不动。
    /// 界面因此稳定停在 81%，不会跟着 80/82 来回跳。
    /// </summary>
    [Fact]
    public void J1_一来一回的抖动_偏离基准的全部按住_基准不动()
    {
        var f = NewFilter();
        f.Seed(81);
        int[] noise = { 80, 81, 82, 81, 80, 82, 81, 82, 80, 81 };
        var expected = new[]
        {
            ReadingVerdict.RejectJitter, ReadingVerdict.Accept,       // 80 按住、81 = 基准
            ReadingVerdict.RejectJitter, ReadingVerdict.Accept,       // 82 按住、81 = 基准
            ReadingVerdict.RejectJitter, ReadingVerdict.RejectJitter, // 80、82 方向反转 → 重新计数
            ReadingVerdict.Accept, ReadingVerdict.RejectJitter,       // 81 = 基准、82
            ReadingVerdict.RejectJitter, ReadingVerdict.Accept,       // 80、81 = 基准
        };
        for (int i = 0; i < noise.Length; i++)
            Assert.Equal(expected[i], f.Evaluate(noise[i], T0.AddMinutes(i + 1)));

        Assert.Equal(81, f.LastAcceptedPercent); // 基准始终没被抖动带走
    }

    /// <summary>真实掉电是**单调**的：第二拍就采信，不会被去抖永久挡住。</summary>
    [Fact]
    public void J2_真实连续掉电_两拍内采信()
    {
        var f = NewFilter();
        f.Seed(81);
        Assert.Equal(ReadingVerdict.RejectJitter, f.Evaluate(80, T0.AddMinutes(1)));
        Assert.Equal(ReadingVerdict.Accept, f.Evaluate(79, T0.AddMinutes(2)));
        Assert.Equal(79, f.LastAcceptedPercent);
    }

    /// <summary>抖动之后出现真实同向变化：按"新的同向序列"重新计数。</summary>
    [Fact]
    public void J3_抖动后回到基准_再同向仍按新序列计数()
    {
        var f = NewFilter();
        f.Seed(81);
        Assert.Equal(ReadingVerdict.RejectJitter, f.Evaluate(80, T0.AddMinutes(1)));
        Assert.Equal(ReadingVerdict.RejectJitter, f.Evaluate(82, T0.AddMinutes(2))); // 反向 → 重新计数
        Assert.Equal(ReadingVerdict.Accept, f.Evaluate(81, T0.AddMinutes(3)));       // 回到基准
        Assert.Equal(ReadingVerdict.RejectJitter, f.Evaluate(80, T0.AddMinutes(4)));
        Assert.Equal(ReadingVerdict.Accept, f.Evaluate(79, T0.AddMinutes(5)));
        Assert.Equal(79, f.LastAcceptedPercent);
    }

    /// <summary>充电时真实的小幅上涨同样按次数采信（与下降对称）。</summary>
    [Fact]
    public void J4_连续上涨也按次数采信()
    {
        var f = NewFilter();
        f.Seed(78);
        Assert.Equal(ReadingVerdict.RejectJitter, f.Evaluate(79, T0.AddMinutes(1)));
        Assert.Equal(ReadingVerdict.Accept, f.Evaluate(80, T0.AddMinutes(2)));
        Assert.Equal(80, f.LastAcceptedPercent);
    }

    /// <summary>协议方可以关掉去抖（<c>jitterConfirmCount: 1</c>），行为回到"幅度之内立即采信"。</summary>
    [Fact]
    public void J5_关闭去抖时恢复旧行为()
    {
        var f = new BatteryReadingFilter(20, TimeSpan.FromMinutes(5), jitterConfirmCount: 1);
        f.Seed(81);
        Assert.Equal(ReadingVerdict.Accept, f.Evaluate(80, T0.AddMinutes(1)));
        Assert.Equal(80, f.LastAcceptedPercent);
    }

    [Fact]
    public void A8_临界值_正好20点要确认_21点走异常窗口()
    {
        // 差 20 仍在幅度内 → 走抖动确认
        var f = NewFilter();
        f.Seed(66);
        Assert.Equal(ReadingVerdict.RejectJitter, f.Evaluate(46, T0.AddMinutes(1)));
        Assert.Equal(ReadingVerdict.Accept, f.Evaluate(46, T0.AddMinutes(2)));

        // 差 21 越界 → 走异常窗口，与抖动无关
        var g = NewFilter();
        g.Seed(66);
        Assert.Equal(ReadingVerdict.RejectAnomaly, g.Evaluate(45, T0.AddMinutes(1)));
    }

    [Fact]
    public void A9_上涨方向_小步需确认_大步先过滤()
    {
        var f = NewFilter();
        f.Seed(60);
        Assert.Equal(ReadingVerdict.RejectJitter, f.Evaluate(75, T0.AddMinutes(1)));
        Assert.Equal(ReadingVerdict.Accept, f.Evaluate(75, T0.AddMinutes(2)));

        var g = NewFilter();
        g.Seed(40);
        Assert.Equal(ReadingVerdict.RejectAnomaly, g.Evaluate(75, T0.AddMinutes(1)));  // 差 35 → 越界
        Assert.Equal(ReadingVerdict.AcceptConfirmed, g.Evaluate(75, T0.AddMinutes(6))); // 持续 5 分钟 → 采信
    }
}
