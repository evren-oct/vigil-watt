using VigilWatt.Core.Hid;
using VigilWatt.Core.Plugins.Protocol;

namespace VigilWatt.Core.Plugins.Betop;

/// <summary>
/// 功 能： 北通身份归一——只认目录显式声明的 group，当前仅鲲鹏20 有线/2.4G 共用一个设备位。
/// 2026/10/25 17:45:16 lym18
/// 2026/11/16 12:00:00 20211 改为目录 group 驱动；不按系列合并、不跨 VID 合并，取舍见 docs/hardware/brands/betop.md §1.8.2。
/// </summary>
internal static class BetopDeviceIdentity
{
    /// <summary>目录里 group 非空的设备返回 <c>{plugin}:{group}</c>；未声明返回 null 走路径哈希。</summary>
    public static string? GroupId(ProtocolEngine engine, HidDeviceInfo info, string pluginPrefix)
    {
        var entry = engine.DeviceOf(info);
        if (entry is null || string.IsNullOrWhiteSpace(entry.Group) || !engine.Matches(info)) return null;
        return $"{pluginPrefix}:{entry.Group.Trim()}";
    }
}
