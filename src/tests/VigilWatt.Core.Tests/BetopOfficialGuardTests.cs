using VigilWatt.Core.Plugins.Betop;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 北通官方软件观测的回归——只记状态变化，不参与认领判定。
/// 2026/10/25 19:42:30 xliu
/// 为什么先观测不拦认领见 docs/hardware/brands/betop.md §1.9。
/// </summary>
public class BetopOfficialGuardTests : IDisposable
{
    public BetopOfficialGuardTests()
    {
        BetopOfficialAppGuard.ResetCache();
    }

    public void Dispose()
    {
        BetopOfficialAppGuard.Probe = () => null;
        BetopOfficialAppGuard.ResetCache();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void IsRunning_TrueWhenOfficialProcessPresent()
    {
        BetopOfficialAppGuard.Probe = () => "BetopGame";

        Assert.True(BetopOfficialAppGuard.IsRunning());
    }

    [Fact]
    public void IsRunning_FalseWhenOfficialProcessAbsent()
    {
        BetopOfficialAppGuard.Probe = () => null;

        Assert.False(BetopOfficialAppGuard.IsRunning());
    }

    /// <summary>探测抛异常不能让观测永久停在"官方在跑"。</summary>
    [Fact]
    public void IsRunning_TreatsProbeFailureAsNotRunning()
    {
        BetopOfficialAppGuard.Probe = () => throw new InvalidOperationException("boom");

        Assert.False(BetopOfficialAppGuard.IsRunning());
    }

    /// <summary>缓存：5 秒内重复查询不重复打进程表（IsCandidate 每个接口都会调一次）。</summary>
    [Fact]
    public void IsRunning_CachesResultWithinWindow()
    {
        int calls = 0;
        BetopOfficialAppGuard.Probe = () =>
        {
            calls++;
            return "BetopGame";
        };

        Assert.True(BetopOfficialAppGuard.IsRunning());
        Assert.True(BetopOfficialAppGuard.IsRunning());
        Assert.True(BetopOfficialAppGuard.IsRunning());

        Assert.Equal(1, calls);
    }

    /// <summary>清缓存后必须重新探测：官方退出、下一轮恢复就靠这条。</summary>
    [Fact]
    public void ResetCache_ForcesReprobe()
    {
        BetopOfficialAppGuard.Probe = () => "BetopGame";
        Assert.True(BetopOfficialAppGuard.IsRunning());

        BetopOfficialAppGuard.Probe = () => null;
        BetopOfficialAppGuard.ResetCache();

        Assert.False(BetopOfficialAppGuard.IsRunning());
    }
}
