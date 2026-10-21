namespace VigilWatt.Core.Plugins.Vgn;

/// <summary>一条 VGN 接口的最小描述（只取"配对"需要的字段，零依赖，便于单测）。</summary>
/// <param name="Path">接口路径（Windows HID 路径，全局唯一）。</param>
/// <param name="Vid">厂商 ID。</param>
/// <param name="Pid">产品 ID。</param>
/// <param name="Product">产品名。</param>
/// <param name="Serial">序列号；同一只设备的多个接口会报同一个值。</param>
public readonly record struct VgnInterfaceRef(string Path, ushort Vid, ushort Pid, string Product, string Serial);

/// <summary>
/// 功 能： 判定"同一只 VGN 鼠标的两种接法"，避免它在设备列表里变成两行互相矛盾的读数。
/// 2026/10/21
///
/// 背景：蜻蜓 F2 大师+ 插线后，Windows 会同时枚举出**两条**厂商接口——
/// 无线接收器（<c>3554:FB56</c>）与插线接口（<c>3554:FB57</c>），
/// 两条各自报告电量，于是界面出现同一只鼠标两行（80% 放电 / 85% 充电中）。
/// F1 V5 同理（<c>391D:1A23</c> 无线 / <c>391D:1026</c> 插线）。
///
/// 判定依据只用**序列号**：产品名会被不同批次复用（两只同型号鼠标会同名），
/// 而同一只设备的两个接口报的是同一个 SN。SN 缺失时**不合并**（宁可多显示一行，
/// 也不要把用户真正的两台设备合并成一台）。
/// </summary>
public static class VgnDevicePairing
{
    /// <summary>Compx 号段：蜻蜓 F2 大师+（y2 协议）。</summary>
    public const ushort VidCompx = 0x3554;

    /// <summary>VGN 自家号段：蜻蜓 F1 V5（kc2 协议）。</summary>
    public const ushort VidVgn = 0x391D;

    /// <summary>每个家族的 (VID, 无线 PID, 插线 PID)。</summary>
    private static readonly (ushort Vid, ushort WirelessPid, ushort WiredPid)[] Families =
    {
        (VidCompx, 0xFB56, 0xFB57),
        (VidVgn, 0x1A23, 0x1026),
    };

    /// <summary>
    /// 返回本轮应当跳过的**无线接口**路径：同一只鼠标插线后，无线与插线两条同时在场。
    /// 保留插线那条——只有它带充电状态（无线那条报的是插线前的旧状态）。
    /// </summary>
    public static IReadOnlyList<string> WirelessPathsToSkip(IReadOnlyList<VgnInterfaceRef> devices)
    {
        var skip = new List<string>();
        if (devices == null || devices.Count == 0) return skip;

        foreach (var family in Families)
        {
            var wireless = devices
                .Where(d => d.Vid == family.Vid && d.Pid == family.WirelessPid)
                .ToList();
            var wired = devices
                .Where(d => d.Vid == family.Vid && d.Pid == family.WiredPid)
                .ToList();
            if (wired.Count == 0 || wireless.Count == 0) continue;

            foreach (var w in wireless)
            {
                // SN 相同才是同一只设备；SN 缺失一律不合并。
                bool paired = wired.Any(x => SameSerial(x, w));
                if (paired) skip.Add(w.Path);
            }
        }

        return skip;
    }

    /// <summary>序列号相同且非空才算同一只设备。</summary>
    public static bool SameSerial(VgnInterfaceRef a, VgnInterfaceRef b)
        => !string.IsNullOrWhiteSpace(a.Serial)
           && !string.IsNullOrWhiteSpace(b.Serial)
           && string.Equals(a.Serial, b.Serial, StringComparison.Ordinal);
}
