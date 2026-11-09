namespace VigilWatt.Core.Models;

public enum DeviceSource
{
    Ble,
    Hid24G,
    BluetoothClassic,

    /// <summary>本机电源（系统电池），非外设。</summary>
    System,
}

public enum ChargingState
{
    Unknown,
    Discharging,
    Charging,
    Full,
}

/// <summary>
/// 功 能： 一台被监控设备的当前状态快照。
/// 2026/10/06 15:53:36 xliu
/// </summary>
public class DeviceStatus
{
    /// <summary>稳定唯一 ID：BLE 用蓝牙地址，2.4G 用 HID 路径哈希 + 设备索引。</summary>
    public required string Id { get; init; }

    public required string Name { get; set; }

    public required DeviceSource Source { get; init; }

    /// <summary>设备类型（选图标用）；插件/监视器声明，未知时 UI 退回按名字猜。</summary>
    public DeviceKind Kind { get; set; } = DeviceKind.Unknown;

    /// <summary>0-100，不可读时为 null。</summary>
    public int? BatteryPercent { get; set; }

    public ChargingState Charging { get; set; } = ChargingState.Unknown;

    /// <summary>协议来源描述（如 "Logitech HID++"），排错用。</summary>
    public string Protocol { get; set; } = "";

    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.Now;

    public bool IsOnline { get; set; } = true;

    /// <summary>指针类遥测（DPI / 回报率）。默认全未知，未适配设备天然就是它。</summary>
    public PointerTelemetry Pointer { get; set; }

    /// <summary>当前数据链路（有线 / 2.4G）。未知时界面回落到来源标签，见 <see cref="DeviceSourceInfo.BadgeLabel"/>。</summary>
    public ConnectionKind Connection { get; set; } = ConnectionKind.Unknown;

    /// <summary>本设备声明过的只读能力。默认空 = 全 Unverified，UI 据此决定能不能说"未获取"。</summary>
    public CapabilitySet Capabilities { get; set; } = CapabilitySet.Empty;

    /// <summary>被认领接口的枚举身份，认领期常量。认领到接收器时它就是接收器，见 <see cref="UsbIdentity"/>。</summary>
    public UsbIdentity? Usb { get; set; }

    /// <summary>协议自报身份。只有实现过自报的插件才有值，其余保持 null（null ≠ 没有型号）。</summary>
    public ReportedIdentity? Reported { get; set; }

    /// <summary>接收器/传输通道身份。只有目录登记过通道的才有值，本体接口恒 null。</summary>
    public ReceiverIdentity? Receiver { get; set; }

    /// <summary>参与统计曲线时用于图例显示的短名；BLE 端点解析不出名字时给可读兜底，别把裸设备 ID 摆到界面上。</summary>
    public string DisplayName => !string.IsNullOrEmpty(Name) ? Name
        : Source == DeviceSource.Ble ? "BLE 设备"
        : Id;

    /// <summary>深拷贝。数据源会复用并原地改写自己的实例，入库前必须快照，否则变化判定比较同一对象恒为假（见 docs/project/architecture.md §6）。</summary>
    public DeviceStatus Clone() => new()
    {
        Id = Id,
        Name = Name,
        Source = Source,
        Kind = Kind,
        BatteryPercent = BatteryPercent,
        Charging = Charging,
        Protocol = Protocol,
        LastUpdated = LastUpdated,
        IsOnline = IsOnline,
        Pointer = Pointer,
        Connection = Connection,
        Capabilities = Capabilities,
        // 三种身份都是 readonly record struct（字段只有 ushort/string），赋值即深拷贝
        Usb = Usb,
        Reported = Reported,
        Receiver = Receiver,
    };

    public override string ToString() => $"{DisplayName} {BatteryPercent?.ToString() ?? "?"}%";
}
