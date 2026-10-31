using VigilWatt.Core.Plugins.Betop;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： BetopPlugin 的 T9 读数稳定判定（滤掉手柄开机瞬态）。
/// 2026/10/31 11:34:00 20211
/// 为什么需要它见 docs/hardware/betop.md §1.4.3。
/// </summary>
public class BetopSettleTests
{
    /// <summary>每次用独立路径：静态状态按路径分桶，共用会让两个用例互相影响。</summary>
    private static string NewPath() => $"test-{Guid.NewGuid():N}";

    /// <summary>注入时刻：判定只比时间差，不必 sleep 真实时间。</summary>
    private static readonly DateTimeOffset T0 = new(2026, 10, 31, 12, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void S1_换值重新计时_同一值持续满窗口才采信()
    {
        string path = NewPath();

        Assert.False(BetopPlugin.IsSettled(path, 20, T0));                      // 首帧：20% 是开机瞬态
        Assert.False(BetopPlugin.IsSettled(path, 73, T0.AddMilliseconds(10)));  // 换值 → 计时重来
        Assert.False(BetopPlugin.IsSettled(path, 73, T0.AddMilliseconds(400)));
        Assert.True(BetopPlugin.IsSettled(path, 73, T0.AddMilliseconds(600)));
    }

    [Fact]
    public void S2_稳定后持续为真_不再抖回去()
    {
        string path = NewPath();
        BetopPlugin.IsSettled(path, 73, T0);

        Assert.True(BetopPlugin.IsSettled(path, 73, T0.AddMilliseconds(600)));
        Assert.True(BetopPlugin.IsSettled(path, 73, T0.AddMilliseconds(700)));
    }

    [Fact]
    public void S3_瞬态差得远时立刻回到未稳定()
    {
        string path = NewPath();
        BetopPlugin.IsSettled(path, 73, T0);
        Assert.True(BetopPlugin.IsSettled(path, 73, T0.AddMilliseconds(600)));

        Assert.False(BetopPlugin.IsSettled(path, 20, T0.AddMilliseconds(610)));  // 差 53 点 → 重新计时
    }
}
