using System.Collections.ObjectModel;

namespace VigilWatt.Core.Models;

/// <summary>
/// 功 能： VigilWatt 对**某台具体设备**已实现并验证过的功能项（不是厂商理论能力）。
/// 2026/11/04 21:05:00 xliu
/// 声明属于设备目录而非协议：同协议多设备共用一份规格，放协议层会串用。见 docs/dev/protocol-json.md §2.1。
/// </summary>
public enum DeviceCapability
{
    /// <summary>读电量。已有真机读数的设备才能标 Supported。</summary>
    BatteryRead = 0,

    /// <summary>读当前生效 DPI 数值。</summary>
    DpiRead,

    /// <summary>读 DPI 档位索引（不是 DPI 数值，也不是档位总数）。</summary>
    DpiLevelRead,

    /// <summary>读设备保存的回报率设置值；不表示实测吞吐率。</summary>
    RateRead,

    /// <summary>写 DPI。与 DpiRead 是两件事：能读不代表能写。</summary>
    DpiWrite,

    /// <summary>写回报率。</summary>
    RateWrite,
}

/// <summary>
/// 功 能： 能力的三态。<see cref="Unverified"/> 是默认值——没有实现不等于已确认不支持。
/// 2026/11/04 21:05:00 xliu
/// </summary>
public enum CapabilityState
{
    /// <summary>尚未验证，或证据不足。**默认态，不许改成 Supported/Unsupported**。</summary>
    Unverified = 0,

    /// <summary>已实现，且有足够证据（真机读数 / 单测钉住的解析）验证。</summary>
    Supported,

    /// <summary>已确认当前 VigilWatt 适配不提供该能力。</summary>
    Unsupported,
}

/// <summary>
/// 功 能： 一台设备的只读能力集合；未声明的项一律 <see cref="CapabilityState.Unverified"/>。
/// 2026/11/04 21:05:00 xliu
/// 不可变：构造后没有任何写入路径，<see cref="Empty"/> 可安全共享。
/// </summary>
public sealed class CapabilitySet
{
    // 用 ReadOnlyDictionary 而不是只把字段声明成 IReadOnlyDictionary：
    // 后者能被强转回 IDictionary 改写，等于"只读"只是口头约定。
    private readonly ReadOnlyDictionary<DeviceCapability, CapabilityState> _declared;

    private CapabilitySet(Dictionary<DeviceCapability, CapabilityState> declared)
        => _declared = new ReadOnlyDictionary<DeviceCapability, CapabilityState>(declared);

    /// <summary>什么也没声明：六项全 Unverified。共享单例，不要在调用点 new 一份。</summary>
    public static CapabilitySet Empty { get; } = new(new Dictionary<DeviceCapability, CapabilityState>());

    /// <summary>显式声明过的项（含显式写 unverified 的），只读；诊断与测试用。</summary>
    public IReadOnlyDictionary<DeviceCapability, CapabilityState> Declared => _declared;

    public bool IsEmpty => _declared.Count == 0;

    /// <summary>取一项能力的状态；未声明返回 Unverified。非法枚举值直接抛，不给"默认值"兜底。</summary>
    public CapabilityState Of(DeviceCapability capability)
    {
        ThrowIfUndefined(capability);
        return _declared.TryGetValue(capability, out var state) ? state : CapabilityState.Unverified;
    }

    public CapabilityState this[DeviceCapability capability] => Of(capability);

    /// <summary>唯一的构造入口：校验枚举与状态，重复声明直接拒绝（不静默取其一）。</summary>
    public static CapabilitySet Create(IEnumerable<KeyValuePair<DeviceCapability, CapabilityState>> declared)
    {
        var map = new Dictionary<DeviceCapability, CapabilityState>();
        foreach (var (capability, state) in declared)
        {
            ThrowIfUndefined(capability);
            if (!Enum.IsDefined(state))
                throw new ArgumentOutOfRangeException(nameof(declared), (int)state,
                    $"能力 {capability} 的状态 {(int)state} 不在三态内");
            if (!map.TryAdd(capability, state))
                throw new ArgumentException($"能力 {capability} 重复声明", nameof(declared));
        }
        return map.Count == 0 ? Empty : new CapabilitySet(map);
    }

    private static void ThrowIfUndefined(DeviceCapability capability)
    {
        // 强转出来的 (DeviceCapability)99 / (DeviceCapability)(-1) 都不是"未声明"，必须报错
        if (!Enum.IsDefined(capability))
            throw new ArgumentOutOfRangeException(nameof(capability), (int)capability, "不是已定义的设备能力");
    }
}
