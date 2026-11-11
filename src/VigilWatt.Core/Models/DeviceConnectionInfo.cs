namespace VigilWatt.Core.Models;

/// <summary>连接徽标的取色档：UI 只按它选颜色，不在 UI 里重新判断链路。</summary>
public enum ConnectionTone
{
    /// <summary>没有证据。**单独一档**，不许与 2.4G 共用颜色。</summary>
    Unknown = 0,

    Wired,
    Wireless24G,
    Bluetooth,

    /// <summary>本机电源：来源即事实，没有"链路"可言。</summary>
    System,
}

/// <summary>
/// 功 能： 连接方式的**唯一展示口径**——设备列表、桌面小组件、删除确认框与未来的详情页都调这里。
/// 2026/11/09 21:50:00 20211
/// 只有 BLE / 经典蓝牙 / 本机允许用来源兜底（那是传输事实）；Hid24G 是接入栈，无值时一律"未知"。
/// </summary>
public static class DeviceConnectionInfo
{
    /// <summary>链路短标签：有线 / 2.4G / 蓝牙 / 未知。</summary>
    public static string Label(ConnectionKind kind) => kind switch
    {
        ConnectionKind.Wired => "有线",
        ConnectionKind.Wireless24G => "2.4G",
        ConnectionKind.Bluetooth => "蓝牙",
        _ => "未知",
    };

    /// <summary>
    /// 体系标签（Bolt / Unifying / 2.4G 接收器）：只回目录登记的展示名，null = 不显示。
    /// 这里**不认识任何品牌**——新体系只改目录 JSON，不改代码。
    /// </summary>
    public static string? ReceiverLabel(ReceiverIdentity? receiver)
        => receiver is { DisplayName: { } name } && !string.IsNullOrWhiteSpace(name) ? name.Trim() : null;

    /// <summary>目录漏填图标时的兜底：图标不能因为少写一个字段就整块消失。</summary>
    public const string DefaultReceiverIconKey = "wireless";

    /// <summary>
    /// 体系图标 key（bolt / unifying / nano / lightning / multi / wireless）：只回目录声明的语义标识，App 按 key 画图。
    /// 没有展示名就没有标签，也就没有图标——两者同生同灭，不出现"有图标没文字"的孤立图形。
    /// </summary>
    public static string? ReceiverIconKey(ReceiverIdentity? receiver)
        => ReceiverLabel(receiver) is null
            ? null
            : receiver is { IconKey: { } key } && !string.IsNullOrWhiteSpace(key)
                ? key.Trim().ToLowerInvariant()
                : DefaultReceiverIconKey;

    /// <summary>徽标短标签：有值按链路；无值只允许蓝牙/本机按来源兜底，其余（含 Hid24G）都是"未知"。</summary>
    public static string Label(TelemetryValue<ConnectionKind> connection, DeviceSource source)
        => IsKnown(connection) ? Label(connection.Value.GetValueOrDefault()) : Fallback(source);

    /// <summary>列表徽标：在线按链路；离线只标"最后 X"或"离线"，不把历史链路写成当前连接。</summary>
    public static string Badge(TelemetryValue<ConnectionKind> connection, DeviceSource source, bool isOnline)
        => isOnline ? Label(connection, source)
            : IsKnown(connection) ? $"最后 {Label(connection.Value.GetValueOrDefault())}" : "离线";

    /// <summary>
    /// 带在线状态的完整描述。离线且无值时不写"最后已知"——没有证据就不该暗示曾经测到过。
    /// </summary>
    public static string Describe(TelemetryValue<ConnectionKind> connection, DeviceSource source, bool isOnline)
    {
        if (isOnline) return Label(connection, source);
        return IsKnown(connection)
            ? $"离线 · 最后已知：{Label(connection.Value.GetValueOrDefault())}"
            : $"离线 · {Label(connection, source)}";
    }

    /// <summary>取色档位：未知单独一档，绝不与 2.4G 共用紫色。</summary>
    public static ConnectionTone Tone(TelemetryValue<ConnectionKind> connection, DeviceSource source)
        => IsKnown(connection)
            ? connection.Value.GetValueOrDefault() switch
            {
                ConnectionKind.Wired => ConnectionTone.Wired,
                ConnectionKind.Wireless24G => ConnectionTone.Wireless24G,
                ConnectionKind.Bluetooth => ConnectionTone.Bluetooth,
                _ => FallbackTone(source),
            }
            : FallbackTone(source);

    /// <summary>Unknown 值即便带了来源也不算链路：展示与合并用同一个判据，免得两处口径分叉。</summary>
    private static bool IsKnown(TelemetryValue<ConnectionKind> connection)
        => connection.HasValue && connection.Value.GetValueOrDefault() != ConnectionKind.Unknown;

    /// <summary>来源兜底只对"来源本身就是链路"的三类生效；Hid24G 是接入栈，不能推断成 2.4G。</summary>
    private static string Fallback(DeviceSource source)
        => source is DeviceSource.Ble or DeviceSource.BluetoothClassic or DeviceSource.System
            ? DeviceSourceInfo.ShortLabel(source)
            : "未知";

    private static ConnectionTone FallbackTone(DeviceSource source) => source switch
    {
        DeviceSource.Ble or DeviceSource.BluetoothClassic => ConnectionTone.Bluetooth,
        DeviceSource.System => ConnectionTone.System,
        _ => ConnectionTone.Unknown,
    };
}
