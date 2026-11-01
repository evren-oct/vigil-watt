using System.Collections.Concurrent;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Rapoo;

/// <summary>
/// 功 能： 雷柏 (Rapoo) 插件 —— 纯被动零写入，按协议族派发（族在 <see cref="RapooFamilies"/>，本类只管认领与转交）。
/// 2026/10/06 15:53:36 xliu
/// 帧布局、充电编码与离线两条腿见 docs/hardware/rapoo.md §1；族清单与目录见 docs/hardware/rapoo-catalog.md §2。
/// </summary>
public sealed class RapooPlugin : BatteryPluginBase
{
    public const ushort RapooVid = 0x24AE;

    /// <summary>首次探测的被动监听窗口 = 实测心跳间隔 3.04 秒的 1.6 倍余量。</summary>
    private static readonly TimeSpan ProbeWindow = TimeSpan.FromSeconds(5);

    /// <summary>已实现的协议族。加一族只改 <see cref="RapooFamilies"/>，本类不动。</summary>
    private readonly RapooFamily[] _families = RapooFamilies.CreateAll();

    public override string Name => "Rapoo";
    public override string DisplayName => "雷柏";
    public override string DisplayNameEn => "RAPOO";

    /// <summary>
    /// 声明 Unknown：雷柏的电量帧挂在键盘集合上，按名字猜会猜错；具体图标由 <see cref="ResolveKind"/> 按协议族给。
    /// 见 docs/hardware/rapoo.md §4.2。
    /// </summary>
    public override DeviceKind Kind => DeviceKind.Unknown;

    /// <summary>含 "Mouse" 产品串的 VID:PID 集合（由 <see cref="OnDevicesScanned"/> 每轮刷新）。</summary>
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

    /// <summary>按协议族查实现；未知协议（还没适配的族）返回 null，由基类当作"本帧不认识"。</summary>
    private RapooFamily? FamilyOf(string? protocol)
        => string.IsNullOrEmpty(protocol)
            ? null
            : _families.FirstOrDefault(f => Array.IndexOf(f.Protocols, protocol) >= 0);

    /// <summary>
    /// 认领判据。**目录优先**：收录过的设备先由目录定族，再用该族的形态判据筛接口——
    /// 这样底座（没有族）在第一步就被挡掉，也不会把鼠标的升级接口当成配置接口。
    /// 未收录的设备回落"任何族认这个形态"，新机型在同族里照样能认出来。
    /// </summary>
    protected override bool IsCandidate(HidDeviceInfo info)
    {
        if (info.VendorId != RapooVid) return false;
        var shape = RapooInterfaceShape.Of(info);

        if (RapooDeviceCatalog.TryGet(info.VendorId, info.ProductId, out var entry))
        {
            var family = FamilyOf(entry.Protocol);
            return family != null && family.MatchesShape(shape);
        }

        return _families.Any(f => f.MatchesShape(shape));
    }

    /// <summary>同步入口（基类要求）：转调异步版本，逻辑只有一份。</summary>
    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => ProbeAsync(handle).GetAwaiter().GetResult();

    /// <summary>
    /// 目录命中的设备**只试它那一个族**（省掉其它族的探测窗口，也避免误认）；
    /// 未收录设备才按注册顺序逐个试，先出读数的算数。
    /// </summary>
    protected override async Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle)
    {
        var shape = RapooInterfaceShape.Of(handle.Info);

        if (RapooDeviceCatalog.TryGet(handle.Info.VendorId, handle.Info.ProductId, out var entry))
        {
            var known = FamilyOf(entry.Protocol);
            if (known == null || !known.MatchesShape(shape)) return null;
            var read = await known.ProbeAsync(handle, ProbeWindow).ConfigureAwait(false);
            return read.HasValue ? (entry.Protocol, read.Value.Percent, read.Value.Charging) : null;
        }

        foreach (var family in _families)
        {
            if (!family.MatchesShape(shape)) continue;
            var read = await family.ProbeAsync(handle, ProbeWindow).ConfigureAwait(false);
            if (read.HasValue) return (family.Protocols[0], read.Value.Percent, read.Value.Charging);
        }
        return null;
    }

    /// <summary>
    /// 读数由后台被动监听负责；轮询不再占 ReadInput（避免与监听抢同一悬空读）。
    /// 沉默离线见 <see cref="OfflinePolicy.Silence"/>。
    /// </summary>
    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
        => FamilyOf(dev.Protocol)?.Poll(RefOf(dev));

    /// <summary>按认领时记下的协议族派发；族返回的链路提示转成离线候选（两条腿见 rapoo.md §3）。</summary>
    protected override (int? Percent, ChargingState Charging, bool Offline)? TryParsePassiveInput(
        TrackedDevice dev, byte[] input)
    {
        var family = FamilyOf(dev.Protocol);
        if (family == null) return null;

        var result = family.ParsePassive(RefOf(dev), input);
        if (result is not { } r) return null;

        // 链路位与电量无关，先处理——电量越界的帧同样要能标记/撤销候选。
        if (r.Link == RapooLinkHint.Down) MarkOfflineCandidate(dev);
        else if (r.Link == RapooLinkHint.Up) ClearOfflineCandidate(dev);

        if (r.Reading is not { } reading) return null;
        return (reading.Percent, reading.Charging, Offline: false);
    }

    /// <summary>协议族拿不到 <see cref="TrackedDevice"/>（protected 嵌套类型），只能摊成值。</summary>
    private static RapooDeviceRef RefOf(TrackedDevice dev)
        => new(dev.Status.Id, dev.Info.DevicePath, dev.Info.ProductId);

    /// <summary>图标按协议族给（一个插件管鼠标/键盘/底座多类设备，见 <see cref="RapooFamily.Kind"/>）。</summary>
    protected override DeviceKind ResolveKind(string protocol)
        => FamilyOf(protocol)?.Kind ?? DeviceKind.Unknown;

    /// <summary>
    /// 显示名：目录命中用官方型号（"雷柏 VT3"）；未收录才退回产品串 + 兄弟接口判鼠标。
    /// 目录来源见 docs/hardware/rapoo-catalog.md §3。
    /// </summary>
    protected override string BuildDisplayName(HidDeviceInfo info)
    {
        if (RapooDeviceCatalog.TryGet(info.VendorId, info.ProductId, out var entry)
            && entry.Model.Length > 0)
            return $"雷柏 {entry.Model}";
        return RapooCodec.DisplayName(info.Product, SiblingIsMouse(info), info.VidPid);
    }

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
        foreach (var family in _families) family.Prune(availableDevices);
        return await base.AttachAsync(availableDevices, claims).ConfigureAwait(false);
    }

    /// <summary>插件被禁用/卸载时清空各族按设备保存的状态。</summary>
    public override void Detach()
    {
        foreach (var family in _families) family.Reset();
        base.Detach();
    }
}
