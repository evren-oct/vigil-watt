namespace VigilWatt.Core.Plugins;
/// <summary>
/// 功 能： 插件共用的路径哈希等小工具。
/// 2026/10/06 15:53:36 xliu
/// </summary>

public static class PluginUtils
{
    public static string OwnerOf(string deviceId)
    {
        int separator = deviceId.IndexOf(':');
        if (separator <= 0) return "";
        var owner = deviceId[..separator];
        return string.Equals(owner, "mchosemouse", StringComparison.OrdinalIgnoreCase) ? "Mchose" : owner;
    }

    /// <summary>FNV-1a 64：把设备路径映射成稳定短 ID（重启、重扫后不变，用于 DeviceStatus.Id）。</summary>
    public static string StableHash(string s)
    {
        ulong h = 1469598103934665603;
        foreach (var c in s) { h ^= c; h *= 1099511628211; }
        return h.ToString("X16");
    }
}
