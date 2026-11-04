using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Protocol;

/// <summary>协议归属：解释器读 JSON / C# 族负责判断 / 目录收录但未适配。</summary>
internal enum SpecImplementation
{
    Data,
    Code,
    None,
}

/// <summary>被动帧里的链路位。</summary>
internal enum LinkState
{
    Unknown,
    Up,
    Down,
}

/// <summary>连接方式（有线 / 2.4G）。</summary>
internal enum LinkMode
{
    Unknown,
    Wireless2G,
    Wired,
}

/// <summary>字段取值宽度。</summary>
internal enum FieldType
{
    U8,
    U16,
    U24,
    U32,
    Bit4,
    Bit,
}

/// <summary>
/// 功 能： 整型匹配条件，JSON 里 <c>19</c> / <c>[65280]</c> / <c>{"min":65}</c> 三种写法都收。
/// 2026/11/01 23:20:00 xliu
/// 接口指纹与长度判据全靠它，写错在加载期报错，不静默不认领。
/// </summary>
internal readonly record struct IntMatch(int? Eq, int? Min, int? Max, int[]? Any, int[]? Not)
{
    public bool Matches(int value) =>
        Not is not null && Array.IndexOf(Not, value) >= 0 ? false
        : Any is not null ? Array.IndexOf(Any, value) >= 0
        : Eq is { } e ? value == e
        : (Min is null || value >= Min) && (Max is null || value <= Max);
}

/// <summary>一条被动/应答帧解析出来的原始事实；判据与反应留给调用方。</summary>
internal readonly record struct FrameReading(
    int? Percent, ChargingState Charging, LinkState Link, LinkMode Mode, PointerTelemetry Pointer);

/// <summary>
/// 一个整型字段：偏移、宽度、可选值域与取值映射。映射表里没有的码视为无值，不拿 0 冒充。
/// 偏移与码表都写在 JSON 里（AGENTS.md §10）。
/// </summary>
internal sealed class FieldSpec
{
    public required int Offset { get; init; }
    public FieldType Type { get; init; } = FieldType.U8;
    public Dictionary<int, int>? ValueMap { get; init; }
    public int? Min { get; init; }
    public int? Max { get; init; }
}

/// <summary>指针遥测字段；整套协议没声明就是 null（绝大多数协议如此）。</summary>
internal sealed class PointerSpec
{
    public FieldSpec? DpiX { get; init; }
    public FieldSpec? DpiY { get; init; }
    public FieldSpec? DpiLevel { get; init; }
    public FieldSpec? DpiLevelCount { get; init; }
    public FieldSpec? ReportRateSettingHz { get; init; }
}

/// <summary>电量字段。</summary>
internal sealed class PercentSpec
{
    public required int Offset { get; init; }
    public FieldType Type { get; init; } = FieldType.U8;

    /// <summary>位域掩码，缺省整字节。用于「电量与标志位同处一字节」的协议（如迈从 CZ 通道）。</summary>
    public int Mask { get; init; } = 0xFF;
}

/// <summary>充电字段：查表 / 非零即充电 / 零即充电，三选一；满电优先由帧级 <see cref="FrameSpec.FullAt"/> 管。</summary>
internal sealed class ChargeSpec
{
    public required int Offset { get; init; }
    public FieldType Type { get; init; } = FieldType.U8;

    /// <summary>位域掩码，缺省整字节；与电量同处一字节时取 bit7 之类。</summary>
    public int Mask { get; init; } = 0xFF;
    public Dictionary<int, ChargingState>? Table { get; init; }
    public bool NonZero { get; init; }
    public bool Zero { get; init; }
    public ChargingState OnMiss { get; init; } = ChargingState.Unknown;
}

/// <summary>链路位。</summary>
internal sealed class LinkSpec
{
    public required int Offset { get; init; }
    public int Bit { get; init; }
    public bool ConnectedWhenSet { get; init; } = true;

    /// <summary>只在某个连接方式下才有意义：有线时没有 RF 链路，bit 恒 0 属正常，不能当断链。</summary>
    public LinkMode? OnlyWhenMode { get; init; }
}

/// <summary>连接方式字段（低 4 位之类的位域 + 映射表）。</summary>
internal sealed class ModeSpec
{
    public required int Offset { get; init; }
    public int Mask { get; init; } = 0x0F;
    public Dictionary<int, LinkMode>? Table { get; init; }
}

/// <summary>
/// 功 能： 一帧的解析规格——从哪个偏移、取哪几个字段、什么算有效范围。
/// 2026/11/01 23:20:00 xliu
/// 只描述"数据长什么样"，不含任何判断；见 docs/dev/protocol-json.md §2。
/// </summary>
internal sealed class FrameSpec
{
    public int[] TryOffsets { get; init; } = [0, 1];
    public int[]? ReportId { get; init; }
    public int? Xor { get; init; }
    public (int Offset, int Value)[] Prefix { get; init; } = [];
    public PercentSpec? Percent { get; init; }
    public ChargeSpec? Charge { get; init; }
    public LinkSpec? Link { get; init; }
    public ModeSpec? Mode { get; init; }
    /// <summary>指针遥测（DPI / 回报率）；只在这套协议确有这些字段时才非 null。</summary>
    public PointerSpec? Pointer { get; init; }
    public IntMatch? Length { get; init; }
    public int MinPercent { get; init; } = 1;
    public int MaxPercent { get; init; } = 100;

    /// <summary>满电阈值：电量达到它一律判"已充满"，优先于充电位。没有充电位的协议也靠它报满电。</summary>
    public int? FullAt { get; init; }
}

/// <summary>主动查询规格：发什么帧、等多久、重试几次、怎么解应答。</summary>
internal sealed class ActiveSpec
{
    public required byte[] Query { get; init; }
    public int WindowMs { get; init; } = 1000;
    public int Attempts { get; init; } = 1;
    public required FrameSpec Response { get; init; }
    public bool RejectQueryEcho { get; init; }
}

/// <summary>接口指纹：一个 HID 接口是不是本协议的通道。</summary>
internal sealed record InterfaceSpec
{
    public IntMatch? UsagePage { get; init; }
    public IntMatch? Usage { get; init; }
    public IntMatch? InputLength { get; init; }
    public IntMatch? OutputLength { get; init; }
    public IntMatch? FeatureLength { get; init; }
    public IntMatch? ReportId { get; init; }
    public string? PathContains { get; init; }

    /// <summary>任一组命中即可（组内仍为 AND）：雷柏把厂商页放在 UsagePage 或 Usage 任一侧。</summary>
    public InterfaceSpec[] AnyOf { get; init; } = [];

    public bool Matches(HidDeviceInfo info)
    {
        if (!MatchesSelf(info)) return false;
        if (AnyOf.Length == 0) return true;
        foreach (var group in AnyOf)
        {
            if (group.MatchesSelf(info)) return true;
        }
        return false;
    }

    private bool MatchesSelf(HidDeviceInfo info)
    {
        if (UsagePage is { } up && !up.Matches(info.UsagePage)) return false;
        if (Usage is { } u && !u.Matches(info.Usage)) return false;
        if (InputLength is { } il && !il.Matches(info.InputReportLength)) return false;
        if (OutputLength is { } ol && !ol.Matches(info.OutputReportLength)) return false;
        if (FeatureLength is { } fl && !fl.Matches(info.FeatureReportLength)) return false;
        if (ReportId is { } rid && !rid.Matches(info.OutputReportId ?? -1)) return false;
        if (PathContains is { Length: > 0 } p
            && !info.DevicePath.Contains(p, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
}

/// <summary>
/// 功 能： 一套协议的完整规格，由 JSON 反序列化而来（文件名与字段见 docs/dev/protocol-json.md）。
/// 2026/11/01 23:20:00 xliu
/// <see cref="Implementation"/> 为 Code 时只提供身份与判据，帧处理由 <see cref="CodeFamily"/> 指的类型负责（抽族前就是插件本身）。
/// </summary>
internal sealed class ProtocolSpec
{
    public required string Name { get; init; }
    public DeviceKind Kind { get; init; } = DeviceKind.Unknown;
    public SpecImplementation Implementation { get; init; } = SpecImplementation.Data;
    /// <summary>实现这套协议的 C# 类型名。**必须是真实存在的类型**——抽族前填插件名，不要预留不存在的族名。</summary>
    public string? CodeFamily { get; init; }
    public bool Pollable { get; init; }
    public bool ChargeDebounce { get; init; }
    public required InterfaceSpec Interface { get; init; }
    public FrameSpec? Passive { get; init; }
    public ActiveSpec? Active { get; init; }
    public int ProbeWindowMs { get; init; } = 5000;
}

/// <summary>目录里的一台设备。型号可为空串。</summary>
internal sealed class DeviceEntry
{
    public required ushort VendorId { get; init; }
    public required ushort ProductId { get; init; }
    public required string Model { get; init; }
    public DeviceKind Kind { get; init; }
    public required string Protocol { get; init; }
    public ushort PairedPid { get; init; }

    /// <summary>本型号自己的能力声明。**不能**放到 ProtocolSpec 上：同协议多设备会串用（docs/dev/protocol-json.md §2.1）。</summary>
    public CapabilitySet Capabilities { get; init; } = CapabilitySet.Empty;
}
