using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins;
using VigilWatt.Core.Utils;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 熔断期"半开探活"的回归测试——设备睡醒后要能被发现，且探活失败不能把退避档位顶高。
/// 2026/10/27 18:40:00 Yang
/// 起因：ATK 鼠标休眠后电量卡住，实机日志显示退避档位被"每睡一次"顶到 4 档（240 秒），
/// 用户动鼠标唤醒后仍要干等 4 分半才刷新。详见 docs/dev/runtime-pitfalls.md §16.7。
/// </summary>
public class BreakerProbeTests : IDisposable
{
    private readonly LogLevel _priorLevel = Log.MinLevel;
    public BreakerProbeTests() => Log.MinLevel = LogLevel.Off;
    public void Dispose() => Log.MinLevel = _priorLevel;

    private static HidDeviceInfo Info(string path)
        => new() { DevicePath = path, Product = "Sleepy mouse", Version = 1 };

    [Fact]
    public async Task ProbeFailureDoesNotRaiseBackoffTier()
    {
        using var plugin = new ProbeFakePlugin();
        await plugin.AttachAsync([Info("dev")], new DeviceClaimRegistry());

        // 三轮失败 → 熔断第 1 档（30 秒）
        plugin.Reply = false;
        plugin.Poll();          // 第 1 轮被 FreshlyClaimed 跳过
        plugin.Poll();
        plugin.Poll();
        plugin.Poll();
        Assert.True(plugin.BreakerTripped);
        Assert.Equal(1, plugin.BreakerTier);

        // 第 4 轮落在熔断期内：走半开探活。失败是预期结果，档位必须原地不动，
        // 否则"睡一觉顶一档"，五档封顶后设备真醒了还要干等 5 分钟。
        plugin.Poll();
        Assert.True(plugin.BreakerTripped);
        Assert.Equal(1, plugin.BreakerTier);
    }

    [Fact]
    public async Task ProbeSuccessClearsBreakerAndComesBackOnline()
    {
        using var plugin = new ProbeFakePlugin();
        await plugin.AttachAsync([Info("dev")], new DeviceClaimRegistry());

        plugin.Reply = false;
        plugin.Poll();          // 第 1 轮被 FreshlyClaimed 跳过
        plugin.Poll();
        plugin.Poll();
        plugin.Poll();
        Assert.True(plugin.BreakerTripped);
        Assert.False(plugin.Online);

        // 设备睡醒：探活拿到应答 → 立即解除熔断、档位归零、重新在线
        plugin.Reply = true;
        var updated = plugin.Poll();
        Assert.False(plugin.BreakerTripped);
        Assert.Equal(0, plugin.BreakerTier);
        Assert.True(plugin.Online);
        Assert.Equal(55, plugin.Percent);
        Assert.Single(updated);
    }

    [Fact]
    public async Task ProbeIsThrottledByInterval()
    {
        using var plugin = new ProbeFakePlugin { ProbeInterval = TimeSpan.FromMinutes(5) };
        await plugin.AttachAsync([Info("dev")], new DeviceClaimRegistry());

        plugin.Reply = false;
        plugin.Poll();          // 第 1 轮被 FreshlyClaimed 跳过
        plugin.Poll();
        plugin.Poll();
        plugin.Poll();
        Assert.Equal(1, plugin.BreakerTier);

        // 熔断后的第一次探活照发（LastBypassAt 是 default，一定满足间隔）
        plugin.Poll();
        var framesAfterFirstProbe = plugin.QueryCount;
        Assert.Equal(1, plugin.BreakerTier);

        // 之后 5 分钟内再轮询：一帧都不发，档位也不动
        plugin.Poll();
        Assert.Equal(framesAfterFirstProbe, plugin.QueryCount);
        Assert.Equal(1, plugin.BreakerTier);
    }

    [Fact]
    public async Task PluginWithoutProbeIntervalKeepsOldBehaviour()
    {
        using var plugin = new NoProbeFakePlugin();
        await plugin.AttachAsync([Info("dev")], new DeviceClaimRegistry());

        plugin.Reply = false;
        plugin.Poll();          // 第 1 轮被 FreshlyClaimed 跳过
        plugin.Poll();
        plugin.Poll();
        plugin.Poll();
        Assert.Equal(1, plugin.BreakerTier);

        // 默认 BreakerProbeInterval = Zero = 关闭：熔断期内一帧都不发，与旧版完全一致
        var framesAfterTrip = plugin.QueryCount;
        plugin.Poll();
        plugin.Poll();
        Assert.Equal(framesAfterTrip, plugin.QueryCount);
        Assert.Equal(1, plugin.BreakerTier);
    }

    [Fact]
    public async Task NudgeHalfOpenWakesDeviceWithoutWaitingForPoll()
    {
        using var plugin = new ProbeFakePlugin();
        await plugin.AttachAsync([Info("dev")], new DeviceClaimRegistry());

        plugin.Reply = false;
        plugin.Poll();          // 第 1 轮被 FreshlyClaimed 跳过
        plugin.Poll();
        plugin.Poll();
        plugin.Poll();
        Assert.True(plugin.BreakerTripped);

        // 15 秒发现节拍上直接探活（不等 60 秒主轮询）
        plugin.Reply = true;
        plugin.NudgeHalfOpen();
        Assert.False(plugin.BreakerTripped);
        Assert.True(plugin.Online);
    }

    /// <summary>照抄 ATK 的 PollDevice 结构：CanWrite 挡住 → 不发帧；发过帧没应答 → 自己报账。</summary>
    private class ProbeFakePlugin : BatteryPluginBase, IDisposable
    {
        public override string Name => "Test";
        public override string DisplayName => "Test";
        public override string DisplayNameEn => "TEST";
        public override DeviceKind Kind => DeviceKind.Mouse;
        public override bool SupportsDiscoveryProbe => true;
        protected override bool ProbeWrites => false;
        protected override bool WritesEnabled => true;
        protected override OfflinePolicy Offline => new() { Silence = TimeSpan.Zero, CountPollFailures = false };

        public TimeSpan ProbeInterval { get; init; } = TimeSpan.FromMilliseconds(1);
        protected override TimeSpan BreakerProbeInterval => ProbeInterval;

        public bool Reply { get; set; } = true;
        public int QueryCount { get; private set; }
        private TrackedDevice? Dev { get; set; }

        // TrackedDevice 是 protected 嵌套类型，测试类不是派生类，只能由插件自己翻译成简单值
        public bool BreakerTripped => Dev!.WritesDisabled;
        public int BreakerTier => Dev!.BreakerTrips;
        public bool Online => Dev!.Status.IsOnline;
        public int? Percent => Dev!.Status.BatteryPercent;

        protected override bool IsCandidate(HidDeviceInfo info) => !info.DevicePath.StartsWith("other:");
        protected override string BuildDeviceId(HidDeviceInfo info) => $"probe:unit-{info.Version}";
        protected override HidDeviceHandle OpenDevice(HidDeviceInfo info)
        {
            var stream = new FileStream(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 0, FileOptions.DeleteOnClose);
            return new HidDeviceHandle(info, stream);
        }
        protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
            => Reply ? ("test", 60, ChargingState.Discharging) : null;

        protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
        {
            Dev = dev;
            if (!CanWrite(dev)) return null;      // 未发帧：不计失败
            QueryCount++;
            if (!Reply)
            {
                ReportPollFailure(dev);
                return null;
            }
            ReportPollAnswered(dev);
            return (55, ChargingState.Discharging);
        }

        public void Dispose() => Detach();
    }

    /// <summary>不覆写 BreakerProbeInterval 的插件：用来证明默认值是"关闭"、旧行为一点没变。</summary>
    private sealed class NoProbeFakePlugin : ProbeFakePlugin
    {
        protected override TimeSpan BreakerProbeInterval => TimeSpan.Zero;
    }
}
