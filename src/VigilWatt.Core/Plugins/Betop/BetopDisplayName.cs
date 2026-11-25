using VigilWatt.Core.Hid;
using VigilWatt.Core.Plugins.Protocol;

namespace VigilWatt.Core.Plugins.Betop;

/// <summary>
/// 功 能： 北通显示名——目录已确认型号优先，其次从产品串识别 KP 系列，最后用通用名。
/// 2026/11/16 12:00:00 20211
/// 产品串里的 Keyboard/Mouse 只是用途后缀，不参与型号判断；见 docs/hardware/brands/betop.md §1.10。
/// </summary>
internal static class BetopDisplayName
{
    /// <summary>目录有确认型号就直接用；歧义 PID 的 model 留空，由产品串分流。</summary>
    public static string For(HidDeviceInfo info, DeviceEntry? entry)
    {
        if (!string.IsNullOrWhiteSpace(entry?.Model))
            return $"北通 {entry.Model.Trim()}";

        var model = ModelFromProduct(info.Product);
        return model is null ? $"北通手柄 ({info.VidPid})" : $"北通 {model}";
    }

    /// <summary>从产品串识别型号；识别不到返回 null，不拿原始串冒充型号。</summary>
    internal static string? ModelFromProduct(string? product)
    {
        if (string.IsNullOrWhiteSpace(product)) return null;
        var s = product.ToUpperInvariant();

        var models = new List<string>(4);
        if (Has(s, "KP20", "KUNPENG 20", "KUNPENG20", "鲲鹏20")) models.Add("鲲鹏20");
        if (Has(s, "KP40", "KUNPENG 40", "KUNPENG40", "鲲鹏40")) models.Add("鲲鹏40");
        if (Has(s, "KP50", "KUNPENG 50", "KUNPENG50", "鲲鹏50")) models.Add("鲲鹏50");
        if (Has(s, "KP70", "KUNPENG 70", "KUNPENG70", "鲲鹏70")) models.Add("鲲鹏70");
        if (HasT9(s)) models.Add("T9");

        if (models.Count == 0) return null;
        return models.All(model => model.StartsWith("鲲鹏", StringComparison.Ordinal))
            ? "鲲鹏" + string.Join("/", models.Select(model => model["鲲鹏".Length..]))
            : string.Join("/", models);
    }

    private static bool Has(string text, params string[] tokens)
        => tokens.Any(token => text.Contains(token, StringComparison.Ordinal));

    /// <summary>T9 要带边界，避免把 T90 之类的型号误认成 T9。</summary>
    private static bool HasT9(string text)
        => text.EndsWith("T9", StringComparison.Ordinal)
           || text.Contains("T9 ", StringComparison.Ordinal)
           || text.Contains("T9-", StringComparison.Ordinal);
}
