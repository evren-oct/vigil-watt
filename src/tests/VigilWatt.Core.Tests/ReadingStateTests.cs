using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins;
using VigilWatt.Core.Plugins.Aula;
using VigilWatt.Core.Plugins.Darkrim;
using VigilWatt.Core.Plugins.HyperX;
using VigilWatt.Core.Plugins.Mchose;
using VigilWatt.Core.Plugins.Protocol;
using VigilWatt.Core.Utils;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 读数落地的字段独立语义——null 电量不覆盖已知电量，Unknown 充电不覆盖已知充电（PR-2）。
/// 2026/11/02 23:25:00 xliu
/// 断言一律看 TrackedDevice.Status 的最终值；被动/核实/半开各走各的生产入口，不绕过。
/// </summary>
public class ReadingStateTests : IDisposable
{
    private readonly LogLevel _priorLevel = Log.MinLevel;
    public ReadingStateTests() => Log.MinLevel = LogLevel.Off;
    public void Dispose() => Log.MinLevel = _priorLevel;

    /// <summary><paramref name="inputLength"/> 决定假句柄每次读多少字节，也决定"一帧一次读"的切分。</summary>
    private static HidDeviceInfo Info(string path, int inputLength = 20)
        => new() { DevicePath = path, Product = "Fake", Version = 1, InputReportLength = (ushort)inputLength };

    private static bool WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(10);
        }
        return condition();
    }

    // ── 轮询入口 ──────────────────────────────────────────────────────

    [Fact]
    public async Task InvalidPercentResponse_PreservesPreviousBatteryState()
    {
        using var plugin = new StateFakePlugin();
        await plugin.AttachAsync([Info("dev")], new DeviceClaimRegistry());

        plugin.Poll();                                    // FreshlyClaimed：本轮跳过
        plugin.Mode = PollMode.InvalidPercent;
        plugin.Poll();

        Assert.Equal(55, plugin.Percent);
        Assert.Equal(ChargingState.Discharging, plugin.Charging);
        Assert.True(plugin.Online);
    }

    [Fact]
    public async Task InvalidPercentResponse_DoesNotTripBreaker()
    {
        using var plugin = new StateFakePlugin();
        await plugin.AttachAsync([Info("dev")], new DeviceClaimRegistry());

        plugin.Poll();
        plugin.Mode = PollMode.InvalidPercent;
        for (int i = 0; i < 5; i++) plugin.Poll();

        Assert.False(plugin.BreakerTripped);
        Assert.Equal(0, plugin.BreakerTier);
        Assert.Equal(0, plugin.Failures);
        Assert.Equal(55, plugin.Percent);
    }

    [Fact]
    public async Task InvalidPercentResponse_DoesNotPushStatus()
    {
        using var plugin = new StateFakePlugin();
        await plugin.AttachAsync([Info("dev")], new DeviceClaimRegistry());
        plugin.Poll();

        int pushes = 0;
        plugin.StatusPushed += _ => pushes++;

        plugin.Mode = PollMode.InvalidPercent;
        plugin.Poll();

        Assert.Equal(0, pushes);
    }

    [Fact]
    public async Task InvalidPercentResponse_WhileOffline_ComesBackOnlineWithOldPercent()
    {
        using var plugin = new StateFakePlugin();
        await plugin.AttachAsync([Info("dev")], new DeviceClaimRegistry());
        plugin.Poll();

        plugin.Mode = PollMode.NoReply;
        for (int i = 0; i < 3; i++) plugin.Poll();
        Assert.False(plugin.Online);

        plugin.Mode = PollMode.InvalidPercent;
        plugin.Poll();

        Assert.True(plugin.Online);
        Assert.Equal(55, plugin.Percent);
    }

    [Fact]
    public async Task ValidPercentResponse_StillUpdates()
    {
        using var plugin = new StateFakePlugin();
        await plugin.AttachAsync([Info("dev")], new DeviceClaimRegistry());
        plugin.Poll();

        plugin.Mode = PollMode.Valid;
        plugin.Poll();

        Assert.Equal(42, plugin.Percent);
        Assert.Equal(ChargingState.Discharging, plugin.Charging);
    }

    [Fact]
    public async Task ChargingOnlyFrame_UpdatesChargingAndKeepsPercent()
    {
        using var plugin = new StateFakePlugin();
        await plugin.AttachAsync([Info("dev")], new DeviceClaimRegistry());
        plugin.Poll();

        plugin.Mode = PollMode.ChargingOnly;
        plugin.Poll();

        Assert.Equal(55, plugin.Percent);
        Assert.Equal(ChargingState.Charging, plugin.Charging);
    }

    [Fact]
    public async Task UnknownChargingWithValidPercent_KeepsPreviousCharging()
    {
        using var plugin = new StateFakePlugin();
        await plugin.AttachAsync([Info("dev")], new DeviceClaimRegistry());
        plugin.Poll();

        plugin.Mode = PollMode.ValidUnknownCharging;
        plugin.Poll();

        Assert.Equal(42, plugin.Percent);
        Assert.Equal(ChargingState.Discharging, plugin.Charging);   // Unknown 不覆盖
    }

    // ── 被动入口（监听线程；两帧预置，逐帧读，无并发写）──────────────

    [Fact]
    public async Task PassiveInvalidPercent_PreservesPreviousBatteryState()
    {
        using var plugin = new StateFakePlugin(passiveListen: true);
        plugin.InitialInput = new byte[] { 0xAA, 0xCC };   // 无效电量帧 + 屏障帧
        await plugin.AttachAsync([Info("dev", inputLength: 1)], new DeviceClaimRegistry());

        Assert.True(WaitUntil(() => plugin.MarkerParsed > 0), "屏障帧未被处理");

        Assert.Equal(55, plugin.Percent);
        Assert.Equal(ChargingState.Discharging, plugin.Charging);
    }

    // ── 核实查询入口（空闲 tick → RunPendingConfirm）──────────────────

    [Fact]
    public async Task ConfirmQueryInvalidPercent_PreservesPreviousBatteryState()
    {
        using var plugin = new StateFakePlugin(passiveListen: true);
        await plugin.AttachAsync([Info("dev")], new DeviceClaimRegistry());

        plugin.Mode = PollMode.InvalidPercent;
        plugin.Poll();                                    // FreshlyClaimed：跳过
        plugin.Poll();                                    // Dev 建立 + 应用一次，产生基线
        var baseline = plugin.LastUpdated;

        plugin.RequestConfirmOnNextPoll = true;
        plugin.Poll();                                    // 只触发 RequestConfirm，不产生读数
        Assert.True(WaitUntil(() => plugin.LastUpdated > baseline), "核实查询未执行");

        Assert.Equal(55, plugin.Percent);
        Assert.True(plugin.Online);
    }

    // ── 半开探活入口（NudgeHalfOpen 同步执行 RunHalfOpenProbe）────────

    [Fact]
    public async Task HalfOpenProbeInvalidPercent_PreservesPercentAndClearsBreaker()
    {
        using var plugin = new StateFakePlugin();
        await plugin.AttachAsync([Info("dev")], new DeviceClaimRegistry());
        plugin.Poll();

        plugin.Mode = PollMode.NoReply;
        for (int i = 0; i < 3; i++) plugin.Poll();
        Assert.True(plugin.BreakerTripped);

        plugin.EnableHalfOpenProbe();
        plugin.Mode = PollMode.InvalidPercent;
        plugin.NudgeHalfOpen();

        Assert.Equal(55, plugin.Percent);
        Assert.False(plugin.BreakerTripped);
        Assert.Equal(0, plugin.BreakerTier);
        Assert.True(plugin.Online);
    }

    [Fact]
    public async Task HalfOpenProbeNoReply_KeepsBreakerAndTier()
    {
        using var plugin = new StateFakePlugin();
        await plugin.AttachAsync([Info("dev")], new DeviceClaimRegistry());
        plugin.Poll();

        plugin.Mode = PollMode.NoReply;
        for (int i = 0; i < 3; i++) plugin.Poll();
        Assert.True(plugin.BreakerTripped);
        Assert.Equal(1, plugin.BreakerTier);

        plugin.EnableHalfOpenProbe();
        plugin.NudgeHalfOpen();

        Assert.Equal(55, plugin.Percent);
        Assert.True(plugin.BreakerTripped);
        Assert.Equal(1, plugin.BreakerTier);
    }

    // ── 厂商回归：真实 codec 产出 → 基类消费 ─────────────────────────
    // 三个插件是 sealed 且未链接进测试工程，这里钉住"codec 给的元组"与"基类怎么用"两端。

    [Fact]
    public async Task HyperX_ChargingOnlyFrame_StillUpdatesCharging()
    {
        var frame = new byte[] { Cf2Codec.EventFrame, Cf2Codec.SubCharge, Cf2Codec.ChargeOn, 0x00 };
        var parsed = Cf2Codec.Parse(frame);
        Assert.NotNull(parsed);
        Assert.Null(parsed!.Value.Percent);
        Assert.Equal(ChargingState.Charging, parsed.Value.Charging);

        using var plugin = new StateFakePlugin(passiveListen: true);
        plugin.InitialInput = Concat(frame, 0xCC);
        plugin.PassiveScript = bytes =>
        {
            var p = Cf2Codec.Parse(bytes);
            return p is null ? null : (p.Value.Percent, p.Value.Charging, p.Value.Offline);
        };
        await plugin.AttachAsync([Info("dev", inputLength: frame.Length)], new DeviceClaimRegistry());

        Assert.True(WaitUntil(() => plugin.MarkerParsed > 0), "屏障帧未被处理");

        Assert.Equal(55, plugin.Percent);
        Assert.Equal(ChargingState.Charging, plugin.Charging);
    }

    [Fact]
    public async Task Darkrim_ChargingOnlyFrame_StillUpdatesCharging()
    {
        var report = new DarkrimCodec.BatteryReport(null, null, null, null, Charging: true);
        Assert.Null(DarkrimCodec.Combine(report));
        Assert.Equal(ChargingState.Charging, DarkrimCodec.MapCharging(null, true));

        using var plugin = new StateFakePlugin(passiveListen: true);
        plugin.InitialInput = new byte[] { 0x01, 0xCC };
        plugin.PassiveScript = _ => (null, DarkrimCodec.MapCharging(null, true), false);
        await plugin.AttachAsync([Info("dev", inputLength: 1)], new DeviceClaimRegistry());

        Assert.True(WaitUntil(() => plugin.MarkerParsed > 0), "屏障帧未被处理");

        Assert.Equal(55, plugin.Percent);
        Assert.Equal(ChargingState.Charging, plugin.Charging);
    }

    [Fact]
    public async Task AulaG9_NoReadingFrame_DoesNotClearPercent()
    {
        var frame = new byte[] { AulaG9Codec.ReportId, 0x47, 0x4D, 0x86, 0x00, 0x00, 0x00, 0x00 };
        Assert.True(AulaG9Codec.TryParse(frame, out var percent, out var charging));
        Assert.Null(percent);
        Assert.Equal(ChargingState.Unknown, charging);

        using var plugin = new StateFakePlugin(passiveListen: true);
        plugin.InitialInput = Concat(frame, 0xCC);
        plugin.PassiveScript = bytes =>
            AulaG9Codec.TryParse(bytes, out var p, out var c) ? (p, c, false) : null;
        await plugin.AttachAsync([Info("dev", inputLength: frame.Length)], new DeviceClaimRegistry());

        Assert.True(WaitUntil(() => plugin.MarkerParsed > 0), "屏障帧未被处理");

        Assert.Equal(55, plugin.Percent);
    }

    // ── 引擎契约：为什么基类必须防 ───────────────────────────────────

    [Fact]
    public void EnginePoll_InvalidPercent_ReturnsAnsweredWithNullPercent()
    {
        // gmouse 形态：应答 percent@8、validPercent 1..100。回 0 → 有应答但无有效电量。
        var spec = MchoseSpecs.Get("gmouse");
        var info = new HidDeviceInfo { DevicePath = "gmouse", InputReportLength = 65, OutputReportLength = 65 };

        // 查询帧会先写进同一句柄（覆盖开头），所以预置成"查询帧 + 应答帧"，写入后正好读到应答。
        var reply = new byte[65];
        reply[0] = 0xAA;   // prefix[0] = 170
        reply[1] = 0x30;   // prefix[1] = 48
        reply[8] = 0x00;   // percent = 0 → 越界

        using var handle = FakeHandle(info, Concat(spec.Active!.Query, reply));
        var outcome = MchoseSpecs.Engine.Poll(
            handle, new DeviceRef("x", "gmouse", 0x3837, 0x4245), "gmouse", writesEnabled: true);

        Assert.Equal(PollAttempt.Answered, outcome.Attempt);
        Assert.Null(outcome.Percent);
    }

    // ── 夹具 ─────────────────────────────────────────────────────────

    private static byte[] Concat(byte[] head, byte tail)
    {
        var all = new byte[head.Length + 1];
        Array.Copy(head, all, head.Length);
        all[head.Length] = tail;
        return all;
    }

    private static byte[] Concat(byte[] head, byte[] tail)
    {
        var all = new byte[head.Length + tail.Length];
        Array.Copy(head, all, head.Length);
        Array.Copy(tail, 0, all, head.Length, tail.Length);
        return all;
    }

    /// <summary>
    /// 用临时文件当假句柄：`HidDeviceHandle` 只接受 FileStream（`HidDeviceHandle.cs:105`），
    /// 所以所有输入帧必须在 Attach 之前预置好，测试期间不再写，避免读写同一句柄的并发问题。
    /// </summary>
    private static HidDeviceHandle FakeHandle(HidDeviceInfo info, byte[] initialInput)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, initialInput);
        var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite,
            FileShare.None, 0, FileOptions.DeleteOnClose);
        return new HidDeviceHandle(info, stream);
    }

    private enum PollMode { Valid, ValidUnknownCharging, InvalidPercent, ChargingOnly, NoReply }

    /// <summary>可编排读数、可注入 codec、可当屏障的假插件；只驱动基类，不含协议知识。</summary>
    private sealed class StateFakePlugin : BatteryPluginBase, IDisposable
    {
        private readonly bool _passive;
        private TimeSpan _breakerInterval = TimeSpan.Zero;

        public StateFakePlugin(bool passiveListen = false) => _passive = passiveListen;

        public override string Name => "Test";
        public override string DisplayName => "Test";
        public override string DisplayNameEn => "TEST";
        public override DeviceKind Kind => DeviceKind.Mouse;

        protected override bool ProbeWrites => false;
        protected override bool WritesEnabled => true;
        protected override bool SupportsPassiveListen => _passive;
        protected override TimeSpan BreakerProbeInterval => _breakerInterval;
        protected override TimeSpan ProbeCooldown => TimeSpan.Zero;
        protected override OfflinePolicy Offline { get; } = new() { Silence = TimeSpan.Zero, CountPollFailures = false };

        public byte[] InitialInput { get; set; } = [];
        public PollMode Mode { get; set; } = PollMode.Valid;
        public Func<byte[], (int? Percent, ChargingState Charging, bool Offline)?>? PassiveScript { get; set; }
        public bool RequestConfirmOnNextPoll { get; set; }

        public int MarkerParsed { get; private set; }

        private TrackedDevice? Dev { get; set; }

        public int? Percent => Dev?.Status.BatteryPercent;
        public ChargingState Charging => Dev?.Status.Charging ?? ChargingState.Unknown;
        public bool Online => Dev?.Status.IsOnline ?? false;
        public bool BreakerTripped => Dev?.WritesDisabled ?? false;
        public int BreakerTier => Dev?.BreakerTrips ?? 0;
        public int Failures => Dev?.ConsecutiveFailures ?? 0;
        public DateTimeOffset LastUpdated => Dev?.Status.LastUpdated ?? default;

        public void EnableHalfOpenProbe() => _breakerInterval = TimeSpan.FromMilliseconds(1);

        protected override bool IsCandidate(HidDeviceInfo info) => true;
        protected override HidDeviceHandle OpenDevice(HidDeviceInfo info) => FakeHandle(info, InitialInput);

        protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
            => ("test", 55, ChargingState.Discharging);

        protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
        {
            Dev = dev;

            if (RequestConfirmOnNextPoll)
            {
                RequestConfirmOnNextPoll = false;
                RequestConfirm(dev);          // 只置触发器：下一拍由 RunPendingConfirm 真去问
                return null;
            }

            switch (Mode)
            {
                case PollMode.Valid:
                    ReportPollAnswered(dev);
                    return (42, ChargingState.Discharging);
                case PollMode.ValidUnknownCharging:
                    ReportPollAnswered(dev);
                    return (42, ChargingState.Unknown);
                case PollMode.InvalidPercent:
                    ReportPollAnswered(dev);
                    return (null, ChargingState.Unknown);
                case PollMode.ChargingOnly:
                    ReportPollAnswered(dev);
                    return (null, ChargingState.Charging);
                default:
                    ReportPollFailure(dev);
                    return null;
            }
        }

        protected override (int? Percent, ChargingState Charging, bool Offline)? TryParsePassiveInput(
            TrackedDevice dev, byte[] input)
        {
            Dev = dev;
            if (input.Length == 1 && input[0] == 0xCC)     // 屏障帧：只记数，不产生读数
            {
                MarkerParsed++;
                return null;
            }
            if (PassiveScript is { } script) return script(input);

            if (input.Length == 0 || input[0] != 0xAA) return null;
            return (null, ChargingState.Unknown, Offline: false);
        }

        public void Dispose() => Detach();
    }
}
