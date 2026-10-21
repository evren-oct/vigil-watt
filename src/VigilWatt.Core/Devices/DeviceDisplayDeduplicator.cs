using VigilWatt.Core.Models;

namespace VigilWatt.Core.Devices;

/// <summary>
/// 功 能： 设备列表的**展示去重**——同一物理设备的多个条目只显示一条。
/// 2026/10/21
///
/// 根因：2.4G 设备的 Id = <c>插件名:StableHash(USB路径)</c>，而同一只设备换个接法
/// （鼠标插线充电 ↔ 无线 dongle）或换 USB 口，路径就变、Id 就变。旧条目离线后靠墓碑机制
/// 在列表里留 24 小时，于是界面上出现"同一只鼠标两行"（一在线一离线）。
///
/// 规则（按显示名分组，**只影响展示，不动存储**——历史/统计仍按各自 Id 记录）：
/// 1. 组内有在线设备 ⇒ 丢弃全部离线条目（在线的保留，全部显示）；
/// 2. 组内全离线 ⇒ 只留最近更新的一条（典型：启动时无线+有线两个墓碑同时复活）。
///
/// 已知取舍：用户真有两台同型号设备且一台在休眠时，休眠那台会被暂时隐藏——
/// 但它本来就不在线、显示的是旧电量，醒来重新上线（Id 虽变但名字相同）即恢复显示。
/// </summary>
public static class DeviceDisplayDeduplicator
{
    /// <summary>
    /// 对已按 (在线优先, 名字) 排序的条目做展示去重，返回的顺序与入参一致。
    /// </summary>
    public static IReadOnlyCollection<DeviceStatus> Dedupe(IEnumerable<DeviceStatus> ordered)
    {
        var list = ordered as IReadOnlyList<DeviceStatus> ?? ordered.ToList();
        var result = new List<DeviceStatus>(list.Count);
        foreach (var group in list.GroupBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var online = group.Where(d => d.IsOnline).ToList();
            if (online.Count > 0)
            {
                result.AddRange(online);
                continue;
            }
            // 全离线：留 LastUpdated 最新的一条（入参顺序不稳定，显式取 max）
            result.Add(group.OrderByDescending(d => d.LastUpdated).First());
        }
        return result;
    }
}
