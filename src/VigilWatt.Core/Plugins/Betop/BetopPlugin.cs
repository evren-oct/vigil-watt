using System.Collections.Concurrent;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Protocol;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Betop;

/// <summary>
/// 功 能： 北通 (BETOP) 手柄插件——品牌目录认领，帧解析全部走协议解释器。
/// 2026/10/06 15:53:36 xliu
/// 协议见 Plugins/Betop/specs/betop.protocols.json；真机结论见 docs/hardware/brands/betop.md §1。
/// </summary>
public sealed class BetopPlugin : BatteryPluginBase
{
    /// <summary>宙斯通道的接口页与报文长；连接方式推断用，不是帧解析。</summary>
    private const ushort ZeusPage = 0xFF00;
    private const int ZeusReportLength = 24;

    /// <summary>广播轮询先纯听 1.5s，听不到再查询 1.5s（探测窗口在 JSON）。</summary>
    private static readonly TimeSpan PollListenWindow = TimeSpan.FromSeconds(1.5);
    private const int PollQueryWindowMs = 1500;

    /// <summary>T9 流 250Hz：开机瞬态低值只存在 29~218 ms，同一读数须稳定这么久才采信。</summary>
    private static readonly TimeSpan T9SettleWindow = TimeSpan.FromMilliseconds(500);

    private readonly ProtocolEngine _engine = ProtocolEngine.Load(
        "VigilWatt.Core.Plugins.Betop.specs.betop.protocols.json",
        "VigilWatt.Core.Plugins.Betop.betop-catalog.json");

    public override string Name => "Betop";
    public override string DisplayName => "北通";
    public override string DisplayNameEn => "BETOP";
    public override DeviceKind Kind => DeviceKind.Gamepad;

    /// <summary>被动推流实时更新；鲲鹏等仍靠轮询。</summary>
    protected override bool SupportsPassiveListen => true;

    /// <summary>沉默不是可靠判据：占位帧立即离线，轮询失败不计数（与迈从同理）。</summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.Zero,
        CountPollFailures = false,
    };

    /// <summary>接收器常在、手柄可能休眠：失败后 30 秒再试，必须小于 60 秒主轮询。</summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(30);

    /// <summary>北通 VID + 引擎匹配；未登记且只有被动协议的接口只识别不探测。</summary>
    protected override bool IsCandidate(HidDeviceInfo info)
    {
        // 观测期：官方软件是否参与 dongle 身份抖动尚无证据，这里只记状态变化，不拦认领。
        BetopOfficialAppGuard.Observe();

        var spec = _engine.Resolve(info);
        if (spec is not null)
        {
            var entry = _engine.DeviceOf(info);
            if (entry is null && spec.Active is null && spec.Passive is not null)
            {
                Log.InfoThrottled($"betop-unregistered-passive:{info.DevicePath}",
                    $"未登记的被动协议接口，仅识别不探测: {info} path={info.DevicePath}",
                    TimeSpan.FromMinutes(10));
                return false;
            }
            return true;
        }

        // 判据不符也要留痕：CR-89 那次用户只看到"没有设备"，日志里一个字都没有
        if (_engine.IsVendor(info.VendorId))
            Log.InfoThrottled($"betop-nomatch:{info.DevicePath}",
                $"北通设备在、但接口不对（未认领）: {info} path={info.DevicePath}", TimeSpan.FromMinutes(10));
        return false;
    }

    /// <summary>测试接缝：直接跑真实候选判定（含门控与日志），避免测试复制策略。</summary>
    internal bool IsCandidateForTest(HidDeviceInfo info) => IsCandidate(info);

    /// <summary>目录显式 group 才归一；其余回退路径哈希（规则见 <see cref="BetopDeviceIdentity"/>）。</summary>
    protected override string BuildDeviceId(HidDeviceInfo info)
        => BetopDeviceIdentity.GroupId(_engine, info, Name.ToLowerInvariant()) ?? base.BuildDeviceId(info);

    /// <summary>能力只认目录里逐型号声明的；未声明一律 Unverified（docs/dev/protocol-json.md §2.1）。</summary>
    protected override CapabilitySet CapabilitiesOf(HidDeviceInfo info) => _engine.CapabilitiesOf(info);

    /// <summary>宙斯 `0xFF00`/24 通道推断为 2.4G；其余接口不推断。</summary>
    protected override TelemetryValue<ConnectionKind> ConnectionOfProbe(HidDeviceInfo info)
        => ProbeInferredConnection(info) is { } kind
            ? TelemetryValue<ConnectionKind>.Inferred(kind, DateTimeOffset.Now)
            : TelemetryValue<ConnectionKind>.Unknown;

    /// <summary>测试接缝：宙斯通道 → 2.4G；其它接口 → null（不推断）。</summary>
    internal static ConnectionKind? ProbeInferredConnection(HidDeviceInfo info)
        => info.UsagePage == ZeusPage && info.InputReportLength == ZeusReportLength
            ? ConnectionKind.Wireless24G
            : null;

    /// <summary>同步入口（基类要求）：转调异步版本，逻辑只有一份。</summary>
    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => ProbeAsync(handle).GetAwaiter().GetResult();

    /// <summary>探测委托引擎：广播先听、静默再查；占位帧认领但离线。</summary>
    protected override async Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle)
    {
        var probe = await _engine.ProbeAsync(handle, WritesEnabled).ConfigureAwait(false);
        if (probe is null) return null;
        if (_engine.ProbeLinkOf(handle.Info) == LinkState.Down)
            return (probe.Value.Protocol, null, ChargingState.Unknown);
        return (probe.Value.Protocol, probe.Value.Percent,
            FinalizeCharging(probe.Value.Charging, probe.Value.Percent));
    }

    /// <summary>轮询：有被动帧的协议先听 1.5s，听不到再查 1.5s；宙斯 pollable=false 不轮询。</summary>
    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (_engine.SpecOf(dev.Protocol) is not { Pollable: true }) return null;

        if (Listen(dev, PollListenWindow) is { } listen)
        {
            if (listen.Link == LinkState.Down)
            {
                RequestOffline(dev);
                return null;
            }
            if (listen.Percent is not { } listenedPercent) return null;
            return (listenedPercent, FinalizeCharging(listen.Charging, listenedPercent));
        }

        if (!CanWrite(dev)) return null;
        var outcome = _engine.Poll(dev.Handle, RefOf(dev), dev.Protocol, WritesEnabled, PollQueryWindowMs);
        if (outcome.Attempt != PollAttempt.Answered || outcome.Percent is not { } queriedPercent) return null;
        return (queriedPercent, FinalizeCharging(outcome.Charging, queriedPercent));
    }

    /// <summary>被动解析：链路 Down → 离线；电量未知保留旧读数；只有 passive-only 协议做 500ms 稳定判定。</summary>
    protected override (int? Percent, ChargingState Charging, bool Offline)? TryParsePassiveInput(
        TrackedDevice dev, byte[] input)
    {
        if (_engine.Parse(RefOf(dev), dev.Protocol, input) is not { } result) return null;
        if (result.Link == LinkState.Down) return (null, ChargingState.Unknown, Offline: true);
        if (result.Percent is not { } percent) return (null, ChargingState.Unknown, Offline: false);

        // status15 是 1Hz 广播，不套 T9 的 250Hz 稳定判定。
        if (_engine.SpecOf(dev.Protocol)?.Active is null && !IsSettled(dev.Info.DevicePath, percent))
            return null;

        return (percent, FinalizeCharging(result.Charging, percent), Offline: false);
    }

    /// <summary>轮询窗口内读帧；只做传输编排，解析仍走引擎。</summary>
    private PassiveOutcome? Listen(TrackedDevice dev, TimeSpan window)
    {
        var deadline = DateTime.UtcNow + window;
        int logged = 0;
        while (DateTime.UtcNow < deadline)
        {
            var input = dev.Handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input is null) continue;

            // 有些厂商集合是 250Hz 持续流，逐帧打日志会刷爆 app.log——只留前几帧。
            if (logged < 4)
            {
                Log.Debug($"北通 收到报文: {Log.Hex(input)}");
                logged++;
            }
            else if (logged == 4)
            {
                Log.Info("北通 该接口在持续推流（非状态响应），后续报文不再逐条记录");
                logged++;
            }

            if (_engine.Parse(RefOf(dev), dev.Protocol, input) is { } result) return result;
        }
        return null;
    }

    /// <summary>满电收尾：100% + 未充电 → Full；100% + 充电中保持 Charging。</summary>
    internal static ChargingState FinalizeCharging(ChargingState state, int? percent)
    {
        if (percent is not { } p) return ChargingState.Unknown;
        if (p >= 100 && state != ChargingState.Charging) return ChargingState.Full;
        return state;
    }

    private sealed class SettleState
    {
        public int Percent;
        public DateTimeOffset Since = DateTimeOffset.MinValue;
    }

    private static readonly ConcurrentDictionary<string, SettleState> SettleByPath = new();

    /// <summary>该读数是否已稳定（换值重新计时）。只用于被动推流；探测/轮询每次只有一帧，判不了。</summary>
    internal static bool IsSettled(string path, int percent) => IsSettled(path, percent, DateTimeOffset.UtcNow);

    /// <summary><paramref name="now"/> 可注入：单测不必 sleep 真实时间。</summary>
    internal static bool IsSettled(string path, int percent, DateTimeOffset now)
    {
        var state = SettleByPath.GetOrAdd(path, _ => new SettleState());
        lock (state)
        {
            if (state.Since == DateTimeOffset.MinValue || state.Percent != percent)
            {
                state.Percent = percent;
                state.Since = now;
                return false;
            }
            return now - state.Since >= T9SettleWindow;
        }
    }

    /// <summary>插件被禁用时顺手复位稳定判定表（Detach 幂等，禁用期间每轮都会被调，见 CR-22）。</summary>
    public override void Detach()
    {
        base.Detach();
        SettleByPath.Clear();
    }

    /// <summary>目录确认型号优先；歧义 PID 由产品串分流，识别不到用通用名。</summary>
    protected override string BuildDisplayName(HidDeviceInfo info)
        => BetopDisplayName.For(info, _engine.DeviceOf(info));

    /// <summary>协议层拿不到 <see cref="TrackedDevice"/>（protected 嵌套类型），只能摊成值。</summary>
    private static DeviceRef RefOf(TrackedDevice dev)
        => new(dev.Status.Id, dev.Info.DevicePath, dev.Info.VendorId, dev.Info.ProductId);
}
