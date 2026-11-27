using VigilWatt.Core.Localization;

namespace VigilWatt.App.Services;

/// <summary>一块活动显示器的快照：GDI 名、稳定身份、友好名、矩形与是否主屏。</summary>
public readonly record struct MonitorScreen(
    string GdiName, string Id, string Label, PixelRect Bounds, PixelRect WorkArea, bool IsPrimary);

/// <summary>
/// 功 能： 按配置挑出贴片该钉的屏幕：稳定身份匹配、型号兜底、目标缺失时降级到主屏。
/// 2026/10/20 16:55:00 xliu
/// 纯函数（零 P/Invoke），直接链进测试项目——"换显示模式后贴片该去哪"必须能在单测里复现。
/// </summary>
public static class MonitorSelection
{
    /// <summary>设置里"跟随主显示器"的取值（也是老配置的默认值）。</summary>
    public const string PrimaryToken = ":primary:";

    /// <summary>老配置的 GDI 名前缀：\\.\DISPLAYn 是当前配置里的序号，不是身份。</summary>
    private const string GdiPrefix = @"\\.\";

    /// <summary>
    /// 解析目标屏幕。<paramref name="note"/> 非空表示发生了降级（配置的屏幕不在，临时贴主屏），
    /// 由调用方决定要不要记日志。
    /// </summary>
    public static MonitorScreen? Resolve(IReadOnlyList<MonitorScreen> screens, string? wanted, out string note)
    {
        note = "";
        if (screens.Count == 0) return null;

        if (string.IsNullOrEmpty(wanted) || wanted.Equals(PrimaryToken, StringComparison.OrdinalIgnoreCase))
            return Primary(screens);

        var hit = Match(screens, wanted);
        // 老配置存的是 \\.\DISPLAYn：只在身份匹配不上时兜底，否则会跟着序号漂到别的屏
        if (hit == null && wanted.StartsWith(GdiPrefix, StringComparison.Ordinal))
            hit = FindByGdiName(screens, wanted);
        if (hit != null) return hit;

        var fallback = Primary(screens);
        if (fallback is { } f)
            note = UiText.Format("S_d44b4cb920", "配置的屏幕当前不在，临时贴到 {0}", f.Label);
        return fallback;
    }

    /// <summary>按身份匹配；同一台显示器换了接口时按型号段兜底，但只在唯一命中时认。</summary>
    public static MonitorScreen? Match(IReadOnlyList<MonitorScreen> screens, string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        foreach (var s in screens)
            if (s.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                return s;

        string? model = ModelKey(id);
        if (model == null) return null;

        MonitorScreen? hit = null;
        foreach (var s in screens)
        {
            if (!string.Equals(ModelKey(s.Id), model, StringComparison.OrdinalIgnoreCase)) continue;
            if (hit != null) return null;   // 两块同型号：分不清就不猜
            hit = s;
        }
        return hit;
    }

    public static MonitorScreen? FindByGdiName(IReadOnlyList<MonitorScreen> screens, string gdiName)
    {
        foreach (var s in screens)
            if (s.GdiName.Equals(gdiName, StringComparison.OrdinalIgnoreCase))
                return s;
        return null;
    }

    public static MonitorScreen? Primary(IReadOnlyList<MonitorScreen> screens)
    {
        foreach (var s in screens)
            if (s.IsPrimary) return s;
        return screens.Count > 0 ? screens[0] : null;
    }

    /// <summary>从 monitorDevicePath 取型号段（<c>\\?\DISPLAY#DEL4189#4&amp;…</c> → <c>DEL4189</c>）。</summary>
    public static string? ModelKey(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var parts = id.Split('#');
        return parts.Length >= 2 && parts[1].Length > 0 ? parts[1] : null;
    }

    /// <summary>设置页下拉框的显示文案：友好名 · 宽×高（主显示器）。</summary>
    public static string Describe(MonitorScreen screen)
    {
        string text = $"{screen.Label} · {screen.Bounds.Width}×{screen.Bounds.Height}";
        return screen.IsPrimary ? text + UiText.Get("S_24b4dbbd69", "（主显示器）") : text;
    }

    /// <summary>GDI 名退化文案：\\.\DISPLAY3 → 显示器 3。</summary>
    public static string FallbackLabel(string gdiName) =>
        gdiName.StartsWith(GdiPrefix, StringComparison.OrdinalIgnoreCase)
            ? UiText.Get("S_1ddb737336", "显示器 ") + gdiName[GdiPrefix.Length..].Replace("DISPLAY", "", StringComparison.OrdinalIgnoreCase)
            : gdiName;
}
