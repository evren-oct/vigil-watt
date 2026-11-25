namespace VigilWatt.Core.Plugins.Rapoo;

/// <summary>
/// 功 能： 雷柏未收录设备的显示名启发式——产品串不带键鼠信息时靠兄弟接口补 Mouse。
/// 2026/11/01 23:20:00 xliu
/// 只是展示逻辑（不是协议数据），所以留代码；已收录设备走目录里的官方型号。见 docs/hardware/brands/rapoo.md §4.2。
/// </summary>
internal static class RapooDisplayName
{
    /// <summary>按产品串与"同 VID:PID 是否有鼠标集合"给名字；空产品串退回品牌 + VIDPID。</summary>
    public static string For(string product, bool siblingIsMouse, string vidPid = "")
    {
        var p = product ?? "";
        if (p.Contains("Mouse", StringComparison.OrdinalIgnoreCase))
            return $"雷柏 {p}";
        if (siblingIsMouse)
        {
            // "Rapoo Gaming Device" 这类设备级产品串不带键鼠信息，认到鼠标就补上 Mouse
            if (p.Contains("keyboard", StringComparison.OrdinalIgnoreCase)
                || p.Contains("Gaming Device", StringComparison.OrdinalIgnoreCase))
                return "雷柏 Rapoo Mouse";
        }
        if (string.IsNullOrEmpty(p))
            return string.IsNullOrEmpty(vidPid) ? "雷柏" : $"雷柏 ({vidPid})";
        return $"雷柏 {p}";
    }
}
