using VigilWatt.Core.Plugins.Delux;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// DELUX 底座充电失真过滤器：数据形态来自真机抓帧（probe/delux-dock-watch*.txt）。
/// 充电中 0x03 电量是装饰值（10 秒虚涨 2-3 点）；0x03→0x01 瞬间有假满电帧，
/// 之后约 6-10 秒虚高残留。窗口取 8 秒。
/// </summary>
public class DockChargeFilterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(double seconds) => T0 + TimeSpan.FromSeconds(seconds);

    [Fact]
    public void F1_放电直通并更新可信值()
    {
        var f = new DockChargeFilter();
        Assert.Equal(80, f.Apply(0x01, 80, At(0)));
        Assert.Equal(79, f.Apply(0x01, 79, At(2)));
    }

    [Fact]
    public void F2_充电中保持最后可信值_装饰值不进历史()
    {
        var f = new DockChargeFilter();
        Assert.Equal(79, f.Apply(0x01, 79, At(0)));
        // 上底座：固件装饰值一路爬升，上报必须保持 79
        Assert.Equal(79, f.Apply(0x03, 79, At(2)));
        Assert.Equal(79, f.Apply(0x03, 82, At(4)));
        Assert.Equal(79, f.Apply(0x03, 91, At(30)));
        Assert.Equal(79, f.Apply(0x03, 100, At(60)));
    }

    [Fact]
    public void F3_充电中没有可信值时放行原值()
    {
        var f = new DockChargeFilter();
        Assert.Equal(65, f.Apply(0x03, 65, At(0)));
    }

    [Fact]
    public void F3b_播种后充电保持播种值()
    {
        var f = new DockChargeFilter();
        f.Seed(77); // 应用重启时鼠标已在底座上
        Assert.Equal(77, f.Apply(0x03, 100, At(0)));
    }

    [Fact]
    public void F4_拿下底座8秒窗口内丢帧_之后恢复信任()
    {
        var f = new DockChargeFilter();
        Assert.Equal(79, f.Apply(0x01, 79, At(0)));
        Assert.Equal(79, f.Apply(0x03, 79, At(2)));   // 上底座
        // 拿下：假满电帧 + 虚高残留全部保持 79
        Assert.Equal(79, f.Apply(0x01, 100, At(4)));  // 假满电帧
        Assert.Equal(79, f.Apply(0x01, 82, At(6)));
        Assert.Equal(79, f.Apply(0x01, 81, At(8)));
        Assert.Equal(79, f.Apply(0x01, 80, At(11.9))); // 窗口内最后一帧
        Assert.Equal(79, f.Apply(0x01, 79, At(12.1))); // 窗口外，恢复信任
    }

    [Fact]
    public void F5_真实久充后高电量能被接受()
    {
        var f = new DockChargeFilter();
        Assert.Equal(40, f.Apply(0x01, 40, At(0)));
        Assert.Equal(40, f.Apply(0x03, 100, At(10)));   // 充了很久，装饰值到 100，仍保持 40
        // 拿下后过渡窗口丢帧
        Assert.Equal(40, f.Apply(0x01, 100, At(12)));
        // 窗口结束后：真实电量确实涨上去了，正常接受
        Assert.Equal(98, f.Apply(0x01, 98, At(21)));
    }

    [Fact]
    public void F6_满电标志保持信任并收尾()
    {
        var f = new DockChargeFilter();
        Assert.Equal(90, f.Apply(0x01, 90, At(0)));
        Assert.Equal(90, f.Apply(0x03, 97, At(2)));
        Assert.Equal(100, f.Apply(0x02, 100, At(4))); // 设备宣布充满：信任
        // 充满后拿下，不走过渡窗口（_charging 已清）
        Assert.Equal(99, f.Apply(0x01, 99, At(6)));
    }

    [Fact]
    public void F7_反复上下底座每轮都过滤()
    {
        var f = new DockChargeFilter();
        Assert.Equal(80, f.Apply(0x01, 80, At(0)));
        // 第一轮
        Assert.Equal(80, f.Apply(0x03, 95, At(10)));
        Assert.Equal(80, f.Apply(0x01, 100, At(12)));
        Assert.Equal(79, f.Apply(0x01, 79, At(21)));
        // 第二轮
        Assert.Equal(79, f.Apply(0x03, 100, At(30)));
        Assert.Equal(79, f.Apply(0x01, 100, At(32)));
        Assert.Equal(78, f.Apply(0x01, 78, At(41)));
    }
}
