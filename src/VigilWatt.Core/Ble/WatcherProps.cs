namespace VigilWatt.Core.Ble;

/// <summary>
/// 功 能： BLE 与经典蓝牙两个 watcher 共用的属性读取（D1 / CR-13）。
/// 2026/10/10 17:44:33 xliu
/// 这两段之前在两个文件里逐字节重复，而"在线判定语义再分叉"正是 2026-10-06 一次 P0 的根因 （一处默认 false 覆盖现状、一处 Removed 不发离线），所以统一到这里。
/// </summary>
public static class WatcherProps
{
    /// <summary>友好名属性。</summary>
    public const string Name = "System.ItemNameDisplay";

    /// <summary>连接状态属性。</summary>
    public const string Connected = "System.Devices.Aep.IsConnected";

    public static string ResolveName(string? currentName, string? deviceName)
        => !string.IsNullOrWhiteSpace(currentName) ? currentName : deviceName?.Trim() ?? "";

    /// <summary>
    /// 读连接状态。<b>属性缺失返回 null，绝不能当成 false</b>：<c>Updated</c> 事件只带发生变化的属性，
    /// 默认 false 会把已连接设备误判离线（真机 2026-10-08：戴尔 WK717 已连接却被后续 Updated 判离线）。
    /// </summary>
    public static bool? ReadConnected(IReadOnlyDictionary<string, object> props)
    {
        if (!props.TryGetValue(Connected, out var obj)) return null;
        try { return Convert.ToBoolean(obj); } catch { return null; }
    }

    /// <summary>从属性袋里取电量：属性名未探明、取不到、类型不对或超出 0~100 一律返回 null（无有效读数）。</summary>
    public static int? ReadBattery(IReadOnlyDictionary<string, object> props, string? batteryProp)
    {
        if (batteryProp == null || !props.TryGetValue(batteryProp, out var obj)) return null;
        try
        {
            int v = Convert.ToInt32(obj);
            return v is > 0 and <= 100 ? v : null;
        }
        catch { return null; }
    }
}
