using VigilWatt.Core.Plugins.Darkrim;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 寸渊影弑让位检测的回归——官方客户端在跑时必须放弃认领。
/// 2026/10/23 19:10:00 zc165
/// 背景见 docs/hardware/protocols.md §13.6。
/// </summary>
public class DarkrimOfficialGuardTests : IDisposable
{
    public DarkrimOfficialGuardTests()
    {
        OfficialAppGuard.ResetCache();
    }

    public void Dispose()
    {
        OfficialAppGuard.Probe = () => false;
        OfficialAppGuard.ResetCache();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void IsRunning_TrueWhenOfficialProcessPresent()
    {
        OfficialAppGuard.Probe = () => true;

        Assert.True(OfficialAppGuard.IsRunning());
    }

    [Fact]
    public void IsRunning_FalseWhenOfficialProcessAbsent()
    {
        OfficialAppGuard.Probe = () => false;

        Assert.False(OfficialAppGuard.IsRunning());
    }

    /// <summary>探测抛异常不能让插件永久停在"让位"状态。</summary>
    [Fact]
    public void IsRunning_TreatsProbeFailureAsNotRunning()
    {
        OfficialAppGuard.Probe = () => throw new InvalidOperationException("boom");

        Assert.False(OfficialAppGuard.IsRunning());
    }

    /// <summary>缓存：5 秒内重复查询不重复打进程表。</summary>
    [Fact]
    public void IsRunning_CachesResultWithinWindow()
    {
        int calls = 0;
        OfficialAppGuard.Probe = () =>
        {
            calls++;
            return true;
        };

        Assert.True(OfficialAppGuard.IsRunning());
        Assert.True(OfficialAppGuard.IsRunning());
        Assert.True(OfficialAppGuard.IsRunning());

        Assert.Equal(1, calls);
    }
}
