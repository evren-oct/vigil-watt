using System.Text.RegularExpressions;

namespace VigilWatt.Core.Plugins.Betop;

/// <summary>
/// 功 能： 北通鲲鹏20 同型号组的设备身份归一（2.4G dongle 与有线本体共用一个设备位）。
/// 2026/10/25 17:45:16 lym18
/// 取舍与缺点（同型号第二只手柄并行、旧曲线断档）见 docs/hardware/brands/betop.md §1.8.2。
/// </summary>
internal static class BetopDeviceIdentity
{
    // 归一只到型号粒度（dongle 与本体无配对关系数据），缺点见 betop.md §1.8.2。
    internal static readonly IReadOnlyDictionary<ushort, string> KunpengGroups = new Dictionary<ushort, string>
    {
        [0x5158] = "kunpeng20",
        [0x5159] = "kunpeng20",
    };

    /// <summary>从 HID 设备路径取 PID（如 vid_20bc&amp;pid_5158 → 0x5158；取不到返回 0）。</summary>
    internal static ushort PidFromPath(string devicePath)
    {
        var m = Regex.Match(devicePath, @"pid_([0-9a-f]{4})", RegexOptions.IgnoreCase);
        return m.Success ? Convert.ToUInt16(m.Groups[1].Value, 16) : (ushort)0;
    }

    /// <summary>
    /// 归一 ID：鲲鹏20 组内返回 <c>{plugin}:kunpeng20</c>（换连接方式同一行、历史连续）；
    /// 组外返回 null（调用方回退路径哈希默认）。
    /// </summary>
    internal static string? KunpengId(string devicePath, string pluginNameLower)
        => KunpengGroups.TryGetValue(PidFromPath(devicePath), out var group)
            ? $"{pluginNameLower}:{group}"
            : null;
}
