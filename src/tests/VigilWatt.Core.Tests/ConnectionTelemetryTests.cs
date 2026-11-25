using VigilWatt.Core.Devices;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 连接方式接入统一遥测后的行为回归——只刷徽标，不动在线、不动电量、不触发重新认领。
/// 2026/11/08 21:20:00 xliu
/// 用假插件钉住基类这条路径（<c>SetConnection</c> / 轮询无读数），不依赖真实 HID 设备。
/// 迈从 0x4D 的映射本身在 <see cref="Mchose0x4DTelemetryTests"/>，这里只验"接进去以后不越界"。
/// </summary>
public sealed class ConnectionTelemetryTests
{
    private static HidDeviceInfo Info(string path)
        => new() { DevicePath = path, Product = "Test mouse", Version = 1 };

    [Fact]
    public async Task ConnectionChangeRefreshesBadgeWithoutTouchingOnlineState()
    {
        using var plugin = new TelemetryFakePlugin();
        var claims = new DeviceClaimRegistry();
        await plugin.AttachAsync([Info("dev")], claims);

        plugin.Poll();                      // 第 1 轮被 FreshlyClaimed 跳过
        plugin.NextConnection = ConnectionKind.Wired;
        plugin.Reading = (80, ChargingState.Discharging);
        plugin.Poll();

        Assert.Equal(ConnectionKind.Wired, plugin.Connection);
        Assert.True(plugin.Online);
        Assert.Equal(80, plugin.Percent);
        Assert.Equal("telemetry:unit-1", plugin.DeviceId);
    }

    /// <summary>Unknown 是"这一帧没带信息"，**不能**把已知的有线/2.4G 洗回未知。</summary>
    [Fact]
    public async Task UnknownConnectionDoesNotOverwriteKnownValue()
    {
        using var plugin = new TelemetryFakePlugin();
        await plugin.AttachAsync([Info("dev")], new DeviceClaimRegistry());

        plugin.Poll();
        plugin.NextConnection = ConnectionKind.Wireless24G;
        plugin.Poll();
        Assert.Equal(ConnectionKind.Wireless24G, plugin.Connection);

        plugin.NextConnection = ConnectionKind.Unknown;
        plugin.Reading = (79, ChargingState.Discharging);
        plugin.Poll();

        Assert.Equal(ConnectionKind.Wireless24G, plugin.Connection);
        Assert.Equal(79, plugin.Percent);
    }

    /// <summary>连接方式变化不该产生一条电量采样（插拔线不是电量事件）。</summary>
    [Fact]
    public void ConnectionOnlyChangeIsNotABatteryChange()
    {
        var stored = Status(ConnectionKind.Wired);
        var incoming = Status(ConnectionKind.Wireless24G);

        Assert.True(DeviceChangeDetector.HasConnectionChanged(stored, incoming));
        Assert.False(DeviceChangeDetector.HasChanged(stored, incoming));
    }

    /// <summary>轮询没拿到读数（设备在睡 / 写失败）：已有电量、充电、连接方式都不能被重置。</summary>
    [Fact]
    public async Task FailedPollKeepsPreviousTelemetry()
    {
        using var plugin = new TelemetryFakePlugin();
        await plugin.AttachAsync([Info("dev")], new DeviceClaimRegistry());

        plugin.Poll();
        plugin.NextConnection = ConnectionKind.Wired;
        plugin.Reading = (66, ChargingState.Charging);
        plugin.Poll();
        Assert.Equal(66, plugin.Percent);
        Assert.Equal(ChargingState.Charging, plugin.Charging);

        plugin.Reading = null;              // 这一轮什么都没读到
        plugin.Poll();

        Assert.Equal(66, plugin.Percent);
        Assert.Equal(ChargingState.Charging, plugin.Charging);
        Assert.Equal(ConnectionKind.Wired, plugin.Connection);
        Assert.True(plugin.Online);         // 单次失败还不到离线阈值
    }

    /// <summary>连接方式变化不改变认领：再扫一轮不会重探，设备 ID 也不变。</summary>
    [Fact]
    public async Task ConnectionChangeDoesNotTriggerReclaim()
    {
        using var plugin = new TelemetryFakePlugin();
        var claims = new DeviceClaimRegistry();
        await plugin.AttachAsync([Info("dev")], claims);
        int probesAfterClaim = plugin.ProbeCount;

        plugin.Poll();
        plugin.NextConnection = ConnectionKind.Wired;
        plugin.Poll();
        Assert.Equal(ConnectionKind.Wired, plugin.Connection);

        await plugin.AttachAsync([Info("dev")], claims);

        Assert.Equal(probesAfterClaim, plugin.ProbeCount);
        Assert.Equal(ConnectionKind.Wired, plugin.Connection);
        Assert.True(claims.IsClaimedBy("dev", "Test"));
    }

    private static DeviceStatus Status(ConnectionKind connection) => new()
    {
        Id = "telemetry:unit-1",
        Name = "Test mouse",
        Source = DeviceSource.Hid24G,
        BatteryPercent = 80,
        Charging = ChargingState.Discharging,
        IsOnline = true,
        Connection = connection == ConnectionKind.Unknown
            ? TelemetryValue<ConnectionKind>.Unknown
            : TelemetryValue<ConnectionKind>.Reported(connection, DateTimeOffset.Now),
    };

    /// <summary>Clone 必须把值、来源、观测时刻一起带走——否则入库快照会把来源洗成 Unknown。</summary>
    [Fact]
    public void CloneKeepsConnectionTelemetry()
    {
        var at = new DateTimeOffset(2026, 11, 9, 21, 0, 0, TimeSpan.FromHours(8));
        var source = Status(ConnectionKind.Wired);
        source.Connection = TelemetryValue<ConnectionKind>.Inferred(ConnectionKind.Wired, at);

        var copy = source.Clone();

        Assert.Equal(ConnectionKind.Wired, copy.Connection.Value);
        Assert.Equal(TelemetrySource.Inferred, copy.Connection.Source);
        Assert.Equal(at, copy.Connection.ObservedAt);
    }

    /// <summary>认领落地就写快照，来源是设备上报——迈从 0x0900 / 雷柏 linkMode 走的正是这条路。</summary>
    [Fact]
    public async Task ClaimSeedsReportedConnection()
    {
        using var plugin = new TelemetryFakePlugin
        {
            ProbeValue = TelemetryValue<ConnectionKind>.Reported(ConnectionKind.Wired, DateTimeOffset.Now),
        };
        await plugin.AttachAsync([Info("dev")], new DeviceClaimRegistry());
        plugin.Poll();                      // 第 1 轮被 FreshlyClaimed 跳过
        plugin.Poll();

        Assert.True(plugin.ConnectionHasValue);
        Assert.Equal(ConnectionKind.Wired, plugin.Connection);
        Assert.Equal(TelemetrySource.DeviceReported, plugin.ConnectionSource);
    }

    /// <summary>推断值经认领落地后来源仍是 Inferred——基类不许把它洗成"设备上报"。</summary>
    [Fact]
    public async Task ClaimKeepsInferredSource()
    {
        using var plugin = new TelemetryFakePlugin
        {
            ProbeValue = TelemetryValue<ConnectionKind>.Inferred(ConnectionKind.Wireless24G, DateTimeOffset.Now),
        };
        await plugin.AttachAsync([Info("dev")], new DeviceClaimRegistry());
        plugin.Poll();
        plugin.Poll();

        Assert.Equal(ConnectionKind.Wireless24G, plugin.Connection);
        Assert.Equal(TelemetrySource.Inferred, plugin.ConnectionSource);
    }

    /// <summary>探测没拿到链路时必须是空遥测，不能落成"Unknown 这个链路"。</summary>
    [Fact]
    public async Task ClaimWithoutProbeConnectionLeavesNoValue()
    {
        using var plugin = new TelemetryFakePlugin();
        await plugin.AttachAsync([Info("dev")], new DeviceClaimRegistry());
        plugin.Poll();
        plugin.Poll();

        Assert.False(plugin.ConnectionHasValue);
        Assert.Equal(ConnectionKind.Unknown, plugin.Connection);
    }

    /// <summary>最小插件：只实现"轮询读一次，顺手报连接方式"，其余交给基类。</summary>
    private sealed class TelemetryFakePlugin : BatteryPluginBase, IDisposable
    {
        public override string Name => "Test";
        public override string DisplayName => "Test";
        public override string DisplayNameEn => "TEST";
        public override DeviceKind Kind => DeviceKind.Mouse;
        protected override bool ProbeWrites => false;
        protected override bool WritesEnabled => true;

        public int ProbeCount { get; private set; }
        public ConnectionKind NextConnection { get; set; } = ConnectionKind.Unknown;

        /// <summary>模拟"探测窗口就拿到了链路"（迈从 0x0900 / 雷柏 linkMode）或"结构推断"的插件。</summary>
        public TelemetryValue<ConnectionKind> ProbeValue { get; set; }
        public (int? Percent, ChargingState Charging)? Reading { get; set; } = (70, ChargingState.Discharging);

        // TrackedDevice 是 protected 嵌套类型，测试类不是派生类，只能由插件自己翻译成简单值
        public ConnectionKind Connection => Dev!.Status.Connection.Value ?? ConnectionKind.Unknown;
        public TelemetrySource ConnectionSource => Dev!.Status.Connection.Source;
        public bool ConnectionHasValue => Dev!.Status.Connection.HasValue;
        public bool Online => Dev!.Status.IsOnline;
        public int? Percent => Dev!.Status.BatteryPercent;
        public ChargingState Charging => Dev!.Status.Charging;
        public string DeviceId => Dev!.Status.Id;

        private TrackedDevice? Dev { get; set; }

        protected override bool IsCandidate(HidDeviceInfo info) => info.DevicePath.StartsWith("dev");
        protected override string BuildDeviceId(HidDeviceInfo info) => $"telemetry:unit-{info.Version}";

        protected override HidDeviceHandle OpenDevice(HidDeviceInfo info)
        {
            var stream = new FileStream(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 0, FileOptions.DeleteOnClose);
            return new HidDeviceHandle(info, stream);
        }

        protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        {
            ProbeCount++;
            return ("test", 70, ChargingState.Discharging);
        }

        protected override TelemetryValue<ConnectionKind> ConnectionOfProbe(HidDeviceInfo info) => ProbeValue;

        protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
        {
            Dev = dev;
            if (Reading is not { } reading) return null;
            SetConnection(dev, NextConnection);
            return reading;
        }

        public void Dispose() => Detach();
    }
}
