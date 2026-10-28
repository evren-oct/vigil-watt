namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 任务栏贴片「选择显示设备」的过滤规则。
/// 2026/10/21 14:05:00 xliu
/// 独立成零依赖纯逻辑（只碰字符串集合），这样过滤口径能在单测里直接复现——
/// 贴片画错时不能只靠肉眼盯任务栏。
/// </summary>
public static class TaskbarDeviceFilter
{
    /// <summary>
    /// 按"选中的设备名"过滤设备显示名列表。
    /// 规则：**一个都不选 = 不过滤**（显示全部在线设备）——设置页里"一个都不勾"是
    /// 用户表达"我不筛选"的自然方式，把它当成"什么都不显示"会让贴片凭空消失。
    /// 名字比对大小写不敏感（设备名来自 HID 报文，大小写不固定）。
    /// </summary>
    public static List<string> Apply(IEnumerable<string> displayNames, IEnumerable<string>? selected)
    {
        var all = displayNames as IList<string> ?? displayNames.ToList();
        if (selected is null) return all.ToList();

        var set = new HashSet<string>(selected.Where(s => !string.IsNullOrWhiteSpace(s)),
            StringComparer.OrdinalIgnoreCase);
        if (set.Count == 0) return all.ToList();

        return all.Where(n => set.Contains(n)).ToList();
    }
}
