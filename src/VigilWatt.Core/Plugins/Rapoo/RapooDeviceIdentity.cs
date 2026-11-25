using VigilWatt.Core.Hid;
using VigilWatt.Core.Plugins.Protocol;

namespace VigilWatt.Core.Plugins.Rapoo;

/// <summary>
/// 功 能： 雷柏同一只鼠标的有线本体 / 2.4G 接收器两条枚举归一——ID 用序列号，同轮优先保留有线。
/// 2026/11/16 12:00:00 20211
/// 为什么不用 PID：有线与无线是两个 PID，且 V700diy-75 LRV 的有线 PID 反而更大；见 docs/hardware/brands/rapoo.md §4.3。
/// </summary>
internal static class RapooDeviceIdentity
{
    /// <summary>同一物理设备的稳定 ID；目录未收录或没有 SN 时返回 null，交回路径哈希。</summary>
    public static string? StableId(ProtocolEngine engine, HidDeviceInfo info, string pluginPrefix)
    {
        if (engine.DeviceOf(info) is not { } entry) return null;
        if (string.IsNullOrWhiteSpace(info.SerialNumber)) return null;
        return $"{pluginPrefix}:{PluginUtils.StableHash(entry.Model + "\u0000" + info.SerialNumber)}";
    }

    /// <summary>配对组键：同组有线/无线归到较小 PID；单 PID 设备返回 null。</summary>
    public static string? GroupKey(DeviceEntry entry)
        => entry.PairedPid == 0
            ? null
            : $"{entry.VendorId:X4}:{Math.Min(entry.ProductId, entry.PairedPid):X4}";

    /// <summary>有线本体可认领时摘掉同组无线接口；基类保留先认领者，否则会丢掉插线后能读的本体（同 ATK）。</summary>
    public static IReadOnlyCollection<string> WirelessPathsToSkip(
        ProtocolEngine engine, IReadOnlyList<HidDeviceInfo> availableDevices)
    {
        var wiredGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in availableDevices)
        {
            if (engine.DeviceOf(info) is not { Wired: true } entry) continue;
            if (!engine.Matches(info)) continue;
            if (GroupKey(entry) is { } key) wiredGroups.Add(key);
        }
        if (wiredGroups.Count == 0) return Array.Empty<string>();

        return availableDevices
            .Where(info => engine.DeviceOf(info) is { Wired: false } entry
                           && GroupKey(entry) is { } key
                           && wiredGroups.Contains(key))
            .Select(info => info.DevicePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
