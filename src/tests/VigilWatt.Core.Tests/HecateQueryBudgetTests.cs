using System.IO;
using System.Runtime.CompilerServices;
using VigilWatt.Core.Plugins.Hecate;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： HECATE 每轮预算的回归测试（CR-100）——关机时不许再堵住共享的插件 pass。
/// 2026/10/29 21:40:00 dotisland
/// 同时锁住"熔断期核实已接线"，否则设备睡醒后仍要干等退避档位（hecate.md §3）。
/// </summary>
public class HecateQueryBudgetTests
{
    /// <summary>关机/休眠是稳定状态：已经连续无应答就别再付重试代价。</summary>
    [Fact]
    public void OfflineDeviceIsQueriedOnce()
        => Assert.Equal(1, HecateQueryBudget.Attempts(halfOpenProbe: false, bypassActive: false, consecutiveFailures: 1));

    /// <summary>半开探活失败是预期结果，且基类要求单台探活 ≤700ms，只能问一次。</summary>
    [Fact]
    public void HalfOpenProbeIsQueriedOnce()
        => Assert.Equal(1, HecateQueryBudget.Attempts(halfOpenProbe: true, bypassActive: false, consecutiveFailures: 0));

    /// <summary>核实轮只问一次：熔断会清零失败计数，只看计数会被当成首次失败而问 2 次
    /// （2026-11-01 实测每个核实轮 464ms，本该 150ms）。</summary>
    [Fact]
    public void BreakerConfirmIsQueriedOnceEvenWhenCounterWasReset()
        => Assert.Equal(1, HecateQueryBudget.Attempts(halfOpenProbe: false, bypassActive: true, consecutiveFailures: 0));

    /// <summary>刚掉线的过渡轮多问一次，滤掉单帧丢包（别把偶发丢包当离线）。</summary>
    [Fact]
    public void HealthyTransitionIsQueriedTwice()
        => Assert.Equal(2, HecateQueryBudget.Attempts(halfOpenProbe: false, bypassActive: false, consecutiveFailures: 0));

    /// <summary>单次查询最坏耗时：500ms 写（与仓库其余写帧一致）+ 150ms 听 = 650ms，
    /// 必须守住基类对半开探活的单台预算 ≤700ms（BatteryPluginBase.RunHalfOpenProbe 注释）。</summary>
    [Fact]
    public void SingleQueryFitsBaseHalfOpenBudget()
        => Assert.True(HecateQueryBudget.WorstCaseQuery <= TimeSpan.FromMilliseconds(700),
            $"单次查询最坏 {HecateQueryBudget.WorstCaseQuery.TotalMilliseconds}ms 超过基类 700ms 预算");

    /// <summary>过渡轮（问 2 次 + 1 个间隔）的最坏耗时，避免"修好稳态又冒出个更长的尾巴"。</summary>
    [Fact]
    public void TransitionRoundStaysBounded()
    {
        var worst = HecateQueryBudget.WorstCaseQuery * 2 + HecateQueryBudget.RetryGap;
        Assert.True(worst <= TimeSpan.FromMilliseconds(1500),
            $"过渡轮最坏 {worst.TotalMilliseconds}ms 超过 1500ms");
    }

    /// <summary>接线断言：轮询必须走 HecateQueryBudget，不许就地写死次数与时长（插件要跑真实 HID，故用源码级断言）。</summary>
    [Fact]
    public void PluginWiresQueryBudget()
    {
        var source = ReadRepoFile("src/VigilWatt.Core/Plugins/Hecate/HecatePlugin.cs");

        Assert.NotEmpty(source);
        Assert.Contains("HecateQueryBudget.Attempts", source);
        Assert.Contains("HecateQueryBudget.ListenWindow", source);
        // 反向锁：原来那个 750ms 听窗口不许回来。
        Assert.DoesNotContain("FromMilliseconds(750)", source);
        // 反向锁：本插件有被动监听（SupportsPassiveListen），按 runtime-pitfalls.md §23.1
        // 第 1 条不开半开探活——CR-53 的 bypass 通道已覆盖唤醒感知，多一条只会抬高写入频率。
        Assert.Contains("SupportsPassiveListen", source);
        Assert.DoesNotContain("BreakerProbeInterval", source);
    }

    private static string ReadRepoFile(string relativePath, [CallerFilePath] string thisFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile) ?? string.Empty);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        return string.Empty;   // 找不到源文件时上面断言会失败，信息更明确
    }
}
