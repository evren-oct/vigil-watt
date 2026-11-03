using System.Collections.Concurrent;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Protocol;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Rapoo;

/// <summary>
/// 功 能： 雷柏 (Rapoo) 插件——纯被动零写入，协议细节全部来自内嵌 JSON，本类只管认领与转交。
/// 2026/10/06 15:53:36 xliu
/// 帧布局、充电编码与离线两条腿见 docs/hardware/rapoo.md §1；协议与目录见 docs/hardware/rapoo-catalog.md §2。
/// </summary>
public sealed class RapooPlugin : BatteryPluginBase
{
    public const ushort RapooVid = 0x24AE;

    private readonly ProtocolEngine _engine = ProtocolEngine.Load(
        "VigilWatt.Core.Plugins.Rapoo.specs.rapoo.protocols.json",
        "VigilWatt.Core.Plugins.Rapoo.rapoo-catalog.json");

    public override string Name => "Rapoo";
    public override string DisplayName => "雷柏";
    public override string DisplayNameEn => "RAPOO";

    /// <summary>
    /// 声明 Unknown：雷柏的电量帧挂在键盘集合上，按名字猜会猜错；具体图标由 <see cref="ResolveKind"/> 按协议给。
    /// 见 docs/hardware/rapoo.md §4.2。
    /// </summary>
    public override DeviceKind Kind => DeviceKind.Unknown;

    /// <summary>含 "Mouse" 产品串的 VID:PID 集合（由 <see cref="AttachAsync"/> 每轮刷新）。</summary>
    private readonly ConcurrentDictionary<string, bool> _mousePids = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 纯被动零写入，探测失败**不是错误**，只说明"鼠标睡着了"（休眠时 dongle 全静音，一动即恢复）。
    /// 必须配合 15 秒快速发现节拍（<see cref="SupportsFastDiscovery"/>）保证每拍都能重探：
    /// 旧值 3 分钟是"键盘出来了、鼠标不出来"的根因（日志实测鼠标要 4 分 09 秒才进列表）。
    /// </summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(5);

    /// <summary>纯被动零写入 → 允许 DeviceManager 用 15 秒节拍做发现（不发任何帧，无风险）。</summary>
    public override bool SupportsFastDiscovery => true;

    /// <summary>本插件不存在任何写入路径；置 false 作为纵深防御（CanWrite 恒 false）。</summary>
    protected override bool WritesEnabled => false;

    /// <summary>探测只被动听心跳、不发任何字节——不受写入开关约束，否则关掉写入会连鼠标都发现不了。</summary>
    protected override bool ProbeWrites => false;

    /// <summary>认领后后台只读心跳帧，插拔电/电量变化立即 StatusPushed。</summary>
    protected override bool SupportsPassiveListen => true;

    /// <summary>
    /// 本插件的离线判定策略（见 <see cref="OfflinePolicy"/>）。
    ///
    /// <b>Confirm = 6 秒</b>：心跳间隔实测 3.04 秒，6 秒 ≈ 2 个周期。不取 3 秒是因为那只有 1 个周期，
    /// 正常丢一帧就会误判，且"断链后无新帧"与"停手静音"在被动路径上无法区分。恢复帧一到候选立即撤销。
    /// <b>Silence = 90 秒</b>：**兜底，不是主要判据**。0x07 是活动驱动的心跳，一停手就静音，
    /// 4 分钟内实测有 5 次 &gt;10 秒的正常空档，短阈值必然误报；90 秒只留给"真关机/没电"。
    /// <b>CountPollFailures = false</b>：零写入，<see cref="BatteryPluginBase.PollDevice"/> 恒返回 null
    /// 是**协议形态**（没有查询命令），不是失联。
    /// </summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Confirm = TimeSpan.FromSeconds(6),
        Silence = TimeSpan.FromSeconds(90),
        CountPollFailures = false,
    };

    /// <summary>认领判据完全由 JSON 的 interface 段决定：目录命中先定协议，未收录才按注册顺序比形态。</summary>
    protected override bool IsCandidate(HidDeviceInfo info) => _engine.Matches(info);

    /// <summary>同步入口（基类要求）：转调异步版本，逻辑只有一份。</summary>
    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => ProbeAsync(handle).GetAwaiter().GetResult();

    /// <summary>探测 = 被动听一个窗口（纯被动协议没有主动查询段），听不到 = 鼠标睡着。</summary>
    protected override Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle)
        => _engine.ProbeAsync(handle, WritesEnabled);

    /// <summary>
    /// 读数由后台被动监听负责；轮询不再占 ReadInput（避免与监听抢同一悬空读）。
    /// 纯被动协议的 pollable 为 false，解释器直接报"没发帧"，不记失败。
    /// </summary>
    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        var outcome = _engine.Poll(dev.Handle, RefOf(dev), dev.Protocol, WritesEnabled);
        return outcome.Attempt == PollAttempt.Answered ? (outcome.Percent, outcome.Charging) : null;
    }

    /// <summary>按认领时记下的协议派发；链路位转成离线候选（两条腿见 rapoo.md §3），遥测随帧落地。</summary>
    protected override (int? Percent, ChargingState Charging, bool Offline)? TryParsePassiveInput(
        TrackedDevice dev, byte[] input)
    {
        if (_engine.Parse(RefOf(dev), dev.Protocol, input) is not { } result) return null;

        // 链路位与电量无关，先处理——电量越界的帧同样要能标记/撤销候选。
        if (result.Link == LinkState.Down) MarkOfflineCandidate(dev);
        else if (result.Link == LinkState.Up) ClearOfflineCandidate(dev);

        if (result.Percent is not { } percent) return null;

        // 只在这条已被电量判据接受的帧上采信遥测：休眠/占位帧结构合法但整帧是 0，DPI 会读成 0
        if (result.Pointer.HasAny) SetPointerTelemetry(dev, result.Pointer);

        return (percent, result.Charging, false);
    }

    /// <summary>协议层拿不到 <see cref="TrackedDevice"/>（protected 嵌套类型），只能摊成值。</summary>
    private static DeviceRef RefOf(TrackedDevice dev)
        => new(dev.Status.Id, dev.Info.DevicePath, dev.Info.VendorId, dev.Info.ProductId);

    /// <summary>图标按协议给（一个插件管鼠标/键盘/底座多类设备，见 JSON 里各协议的 kind）。</summary>
    protected override DeviceKind ResolveKind(string protocol) => _engine.KindOf(protocol);

    /// <summary>
    /// 显示名：目录命中用官方型号（"雷柏 VT3"）；未收录才退回产品串 + 兄弟接口判鼠标。
    /// 目录来源见 docs/hardware/rapoo-catalog.md §3。
    /// </summary>
    protected override string BuildDisplayName(HidDeviceInfo info)
        => _engine.DisplayNameOf(info) ?? RapooDisplayName.For(info.Product, SiblingIsMouse(info), info.VidPid);

    /// <summary>同 <c>VID:PID</c> 下是否存在标准鼠标集合（usage 0001:0002）的兄弟接口。</summary>
    private bool SiblingIsMouse(HidDeviceInfo info)
        => _mousePids.ContainsKey($"{info.VendorId:X4}:{info.ProductId:X4}");

    /// <summary>
    /// 认领前先重建"哪些 VID:PID 带鼠标集合"的索引。判据用 **usage 0001:0002**（不是产品串）：
    /// 接收器模式的产品串是设备级的 "Rapoo Gaming Device"，不含 Mouse。
    /// </summary>
    public override async Task<IReadOnlyList<DeviceStatus>> AttachAsync(
        IReadOnlyList<HidDeviceInfo> availableDevices, DeviceClaimRegistry claims)
    {
        _mousePids.Clear();
        foreach (var d in availableDevices)
        {
            if (d.VendorId != RapooVid) continue;
            if (d.UsagePage == 0x0001 && d.Usage == 0x0002)
                _mousePids[$"{d.VendorId:X4}:{d.ProductId:X4}"] = true;
        }
        _engine.Prune(availableDevices);
        return await base.AttachAsync(availableDevices, claims).ConfigureAwait(false);
    }

    /// <summary>插件被禁用/卸载时清空解释器按设备保存的状态。</summary>
    public override void Detach()
    {
        _engine.Reset();
        base.Detach();
    }
}
