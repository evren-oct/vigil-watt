using System.Collections.Concurrent;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Protocol;

/// <summary>轮询三态：没发帧 / 有应答 / 发了没应答。取代按协议名字符串特判。</summary>
internal enum PollAttempt
{
    NotAttempted,
    Answered,
    Failed,
}

/// <summary>一次轮询的结果。</summary>
internal readonly record struct PollOutcome(PollAttempt Attempt, int? Percent, ChargingState Charging);

/// <summary>一次被动帧解析的结果；电量可为 null（帧有效但电量越界），链路位仍然可信。</summary>
internal readonly record struct PassiveOutcome(
    int? Percent, ChargingState Charging, LinkState Link, PointerTelemetry Pointer);

/// <summary>插件视角的设备标识：协议层拿不到 <c>TrackedDevice</c>，只能摊成值。</summary>
internal readonly record struct DeviceRef(string Id, string DevicePath, ushort VendorId, ushort ProductId);

/// <summary>
/// 功 能： 协议解释器——把 JSON 规格变成认领判据、探测、轮询与被动解析，品牌插件只做委托。
/// 2026/11/01 23:20:00 xliu
/// 判断与状态（去抖、离线候选、握手）不在这里：它们属于插件或 <c>codeFamily</c>。见 docs/dev/protocol-json.md。
/// </summary>
internal sealed class ProtocolEngine
{
    private readonly SpecBundle _bundle;

    /// <summary>按设备路径隔离的充电去抖状态（R3：多台设备共用一份会互相串味）。</summary>
    private readonly ConcurrentDictionary<string, ChargingState> _lastCharging =
        new(StringComparer.OrdinalIgnoreCase);

    private ProtocolEngine(SpecBundle bundle) => _bundle = bundle;

    public static ProtocolEngine Load(string protocolsResource, string devicesResource)
        => new(ProtocolSpecLoader.Load(protocolsResource, devicesResource));

    /// <summary>测试接缝：与 <see cref="Load"/> 走同一加载核心，直接喂两份 JSON 文本。</summary>
    internal static ProtocolEngine FromJson(string protocolsJson, string devicesJson)
        => new(ProtocolSpecLoader.LoadFromJson(protocolsJson, devicesJson));

    /// <summary>显示名前缀（"雷柏"），来自 devices.json 的 brand 字段。</summary>
    public string Brand => _bundle.Brand;

    /// <summary>已收录的设备条数（单测用）。</summary>
    public int DeviceCount => _bundle.Devices.Count;

    /// <summary>已注册的协议条数（单测用）。</summary>
    public int ProtocolCount => _bundle.Protocols.Count;

    /// <summary>
    /// 认领判据。<b>目录优先</b>：收录过的设备先由目录定协议，再校验接口形态——
    /// 底座与旁路接口在第一步就被挡掉；未收录设备才按注册顺序取第一个形态相符的协议。
    /// </summary>
    public ProtocolSpec? Resolve(HidDeviceInfo info)
    {
        if (_bundle.DeviceSpecs.TryGetValue((info.VendorId, info.ProductId), out var known))
        {
            if (known.Implementation == SpecImplementation.None) return null;
            return known.Interface.Matches(info) ? known : null;
        }

        if (!_bundle.Vendors.Contains(info.VendorId)) return null;
        foreach (var spec in _bundle.Ordered)
        {
            if (spec.Implementation == SpecImplementation.None) continue;
            if (spec.Interface.Matches(info)) return spec;
        }
        return null;
    }

    public bool Matches(HidDeviceInfo info) => Resolve(info) is not null;

    public ProtocolSpec? SpecOf(string protocol) => _bundle.Protocols.GetValueOrDefault(protocol);

    public DeviceEntry? DeviceOf(HidDeviceInfo info)
        => _bundle.Devices.GetValueOrDefault((info.VendorId, info.ProductId));

    /// <summary>
    /// 设备能力查询：**纯配置查询**，不开句柄、不发帧、不动解析状态与去抖。判据与认领同源，
    /// 接口不符 / implementation=none / 未收录一律 Unverified；非法枚举值仍然抛，不拿默认值兜底。
    /// </summary>
    public CapabilityState CapabilityOf(HidDeviceInfo info, DeviceCapability capability)
        => DeclaredCapabilities(info).Of(capability);

    /// <summary>整块能力：认领时落到 <see cref="DeviceStatus"/>，UI 靠它区分"能读但还没读到"与"不支持"。</summary>
    public CapabilitySet CapabilitiesOf(HidDeviceInfo info) => DeclaredCapabilities(info);

    /// <summary>目录按 VID/PID 索引，但同一台设备的鼠标集合、底座集合不参与认领，不能借用目录里的声明。</summary>
    private CapabilitySet DeclaredCapabilities(HidDeviceInfo info)
        => Resolve(info) is null
            ? CapabilitySet.Empty
            : DeviceOf(info)?.Capabilities ?? CapabilitySet.Empty;

    public DeviceKind KindOf(string protocol) => SpecOf(protocol)?.Kind ?? DeviceKind.Unknown;

    /// <summary>目录命中且型号非空时给官方名；未收录返回 null，由插件自己回落。</summary>
    public string? DisplayNameOf(HidDeviceInfo info)
    {
        var entry = DeviceOf(info);
        if (entry is null || entry.Model.Length == 0) return null;
        return string.IsNullOrEmpty(Brand) ? entry.Model : $"{Brand} {entry.Model}";
    }

    /// <summary>首次探测：先被动听一个窗口，听不到再按 active 规格主动问（零写入协议只有前半段）。</summary>
    public async Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle, bool writesEnabled)
    {
        var spec = Resolve(handle.Info);
        if (spec is null || spec.Implementation != SpecImplementation.Data) return null;

        if (spec.Passive is { } passive
            && await ListenAsync(handle, spec.ProbeWindowMs, passive).ConfigureAwait(false) is { } listened)
            return (spec.Name, listened.Percent, listened.Charging);

        if (spec.Active is { } active && writesEnabled
            && await QueryRunner.RunAsync(handle, active).ConfigureAwait(false) is { } answered)
            return (spec.Name, answered.Percent, answered.Charging);

        return null;
    }

    /// <summary>轮询。没发帧与发了没应答必须分开，否则零写入协议会被误记成失败。</summary>
    public PollOutcome Poll(HidDeviceHandle handle, DeviceRef dev, string protocol, bool writesEnabled)
    {
        var spec = SpecFor(dev, protocol);
        if (spec is null || spec.Implementation != SpecImplementation.Data || !spec.Pollable)
            return new PollOutcome(PollAttempt.NotAttempted, null, ChargingState.Unknown);
        if (spec.Active is not { } active || !writesEnabled)
            return new PollOutcome(PollAttempt.NotAttempted, null, ChargingState.Unknown);

        return QueryRunner.Run(handle, active) is { } reading
            ? new PollOutcome(PollAttempt.Answered, reading.Percent, ApplyDebounce(spec, dev, reading))
            : new PollOutcome(PollAttempt.Failed, null, ChargingState.Unknown);
    }

    /// <summary>被动帧解析。帧结构不匹配返回 null；电量越界的帧返回 Percent=null 但保留链路位与遥测。</summary>
    public PassiveOutcome? Parse(DeviceRef dev, string protocol, byte[] frame)
    {
        var spec = SpecFor(dev, protocol);
        if (spec?.Passive is not { } passive) return null;
        if (FrameReader.Read(passive, frame) is not { } reading) return null;
        // 观测时刻在这里补：FrameReader 是纯函数不带时钟，而遥测值必须有真实时间（否则永不过期）
        return new PassiveOutcome(reading.Percent, ApplyDebounce(spec, dev, reading), reading.Link,
            reading.Pointer.WithObservedAt(DateTimeOffset.Now));
    }

    /// <summary>设备拔掉后清掉它的去抖状态，防随插拔累积。</summary>
    public void Prune(IReadOnlyList<HidDeviceInfo> present)
    {
        var alive = present.Select(d => d.DevicePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in _lastCharging.Keys.Where(k => !alive.Contains(k)).ToList())
            _lastCharging.TryRemove(key, out _);
    }

    /// <summary>插件被禁用/卸载时清空全部状态。</summary>
    public void Reset() => _lastCharging.Clear();

    /// <summary>设备有覆盖规格（偏移差一位那种）时优先用它，否则回落协议默认。</summary>
    private ProtocolSpec? SpecFor(DeviceRef dev, string protocol)
        => _bundle.DeviceSpecs.TryGetValue((dev.VendorId, dev.ProductId), out var overridden)
            ? overridden
            : SpecOf(protocol);

    /// <summary>充电去抖：连续两帧都是"充电中"才判充电，满电一律判充满（见 docs/hardware/rapoo.md §5.2）。</summary>
    private ChargingState ApplyDebounce(ProtocolSpec spec, DeviceRef dev, FrameReading reading)
    {
        if (!spec.ChargeDebounce || reading.Percent is null) return reading.Charging;

        var previous = _lastCharging.TryGetValue(dev.DevicePath, out var last) ? last : ChargingState.Unknown;
        _lastCharging[dev.DevicePath] = reading.Charging;

        if (reading.Charging == ChargingState.Full) return ChargingState.Full;
        return reading.Charging == ChargingState.Charging && previous == ChargingState.Charging
            ? ChargingState.Charging
            : ChargingState.Discharging;
    }

    /// <summary>被动听一个窗口，首个解析成功的帧立即返回。不发任何字节。</summary>
    private static async Task<FrameReading?> ListenAsync(HidDeviceHandle handle, int windowMs, FrameSpec spec)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(windowMs);
        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            var slice = remaining < TimeSpan.FromMilliseconds(400) ? remaining : TimeSpan.FromMilliseconds(400);
            if (slice <= TimeSpan.Zero) break;

            var input = await handle.ReadInputAsync(slice).ConfigureAwait(false);
            if (input is null) continue;
            if (FrameReader.Read(spec, input) is { } reading) return reading;
        }
        return null;
    }
}
