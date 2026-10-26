namespace VigilWatt.Core.Plugins.Atk;

/// <summary>一条 ATK 接口的最小描述（只取"配对"需要的字段，零依赖，便于单测）。</summary>
/// <param name="Path">接口路径（Windows HID 路径，全局唯一）。</param>
/// <param name="Pid">产品 ID。</param>
/// <param name="Product">产品名。</param>
/// <param name="Serial">序列号；同一只设备的多个接口会报同一个值（可能为空）。</param>
public readonly record struct AtkInterfaceRef(string Path, ushort Pid, string Product, string Serial);

/// <summary>
/// 功 能： 判定"同一只 ATK 鼠标的两种接法"，插线后接收器与本体两条同时在场时只留插线那条。
/// 2026/10/26 22:40:03 ranxue
///
/// 背景：ATK A9 Air 大师版+ 用 USB-C 数据线插上、同时保留 GEM 8K 接收器时，Windows 会同时枚举出
/// <c>373B:1278</c>（接收器）与 <c>373B:1293</c>（本体）两套接口。实测此时**只有本体应答**
/// （85%、充电中），接收器那条链路不应答——鼠标固件切到了有线。旧逻辑"接收器在位就不认领本体"
/// 于是什么读数都出不来（2026-10-26 实机复现，见 atk.md §3.1）。
///
/// 反过来的顺序也不成立：接收器报的是插线前的旧状态、不带充电位，而且产品名是通用串
/// （<c>Wireless mouse 8k dongle-L</c>），拿不到机型。所以**插线形态优先**。
///
/// 配对依据是**型号归一组**（接收器 PID ↔ 本体 PID 的实测配对表）。ATK 的接收器与本体
/// 确实报同一个 USB 序列号，但扫描器（<c>HidDeviceScanner</c>）目前不采 SN，所以 SN 只能当
/// 加分项用：两边都有 SN 且不同 → 判为两只不同鼠标，不合并；任一边缺 → 按型号组配对。
/// </summary>
public static class AtkDevicePairing
{
    /// <summary>
    /// 每个归一组：(组名, 接收器 PID, 本体 PID)。组名与 <c>AtkPlugin</c> 的设备 ID 一致。
    /// 实测配对来源：F1 Pro Max / F1 V3 ULTIMATE+ / A9 Air 大师版+ 各一套真机（atk.md §4）。
    /// </summary>
    private static readonly (string Group, ushort[] Receivers, ushort[] Bodies)[] Families =
    {
        ("promax", new ushort[] { 0x101B, 0x101A }, new ushort[] { 0x1015 }),
        ("v3",     new ushort[] { 0x1303 },          new ushort[] { 0x12A9 }),
        ("a9air",  new ushort[] { 0x1278 },          new ushort[] { 0x1293 }),
    };

    /// <summary>该 PID 属于哪个归一组；未登记返回 <c>null</c>。</summary>
    public static string? GroupOf(ushort pid)
    {
        foreach (var f in Families)
        {
            if (Array.IndexOf(f.Receivers, pid) >= 0 || Array.IndexOf(f.Bodies, pid) >= 0)
                return f.Group;
        }
        return null;
    }

    /// <summary>该 PID 是否是**接收器**（无线形态）。</summary>
    public static bool IsReceiver(ushort pid)
    {
        foreach (var f in Families)
        {
            if (Array.IndexOf(f.Receivers, pid) >= 0) return true;
        }
        return false;
    }

    /// <summary>
    /// 返回本轮应当跳过的**接收器**接口路径：同一组的本体插线在场时，接收器那条不再认领。
    /// 只保留本体——只有它带充电状态，且产品名就是机型。
    /// </summary>
    public static IReadOnlyList<string> ReceiverPathsToSkip(IReadOnlyList<AtkInterfaceRef> devices)
    {
        var skip = new List<string>();
        if (devices is null || devices.Count == 0) return skip;

        foreach (var family in Families)
        {
            var receivers = devices.Where(d => Array.IndexOf(family.Receivers, d.Pid) >= 0).ToList();
            if (receivers.Count == 0) continue;

            var bodies = devices.Where(d => Array.IndexOf(family.Bodies, d.Pid) >= 0).ToList();
            if (bodies.Count == 0) continue;   // 没插线 → 接收器照常认领

            foreach (var r in receivers)
            {
                if (bodies.Any(b => SameDevice(b, r))) skip.Add(r.Path);
            }
        }

        return skip;
    }

    /// <summary>
    /// 两条接口是否同一只鼠标。两边都报了序列号时必须相同；
    /// 任一边缺序列号时按"同型号归一组"判定（扫描器目前不采 SN）。
    /// </summary>
    public static bool SameDevice(AtkInterfaceRef a, AtkInterfaceRef b)
    {
        bool hasA = !string.IsNullOrWhiteSpace(a.Serial);
        bool hasB = !string.IsNullOrWhiteSpace(b.Serial);
        if (hasA && hasB) return string.Equals(a.Serial, b.Serial, StringComparison.Ordinal);
        return true;
    }
}
