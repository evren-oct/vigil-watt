using System.Globalization;

namespace VigilWatt.Core.Localization;
/// <summary>
/// 功 能：界面语言的取值归一化与首次启动的系统语言判定。
/// 2026/11/28 dogs0
/// zh-CN 是唯一内置回退语言：未配置或系统语言不受支持时都用它。
/// </summary>
public static class AppLanguage
{
    public const string Chinese = "zh-CN";
    public const string Russian = "ru-RU";
    public const string Auto = "";

    /// <summary>把配置值收敛到支持的语言；空串保留为“跟随系统”，未知值回退 zh-CN。</summary>
    public static string Normalize(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0 || text.Equals("auto", StringComparison.OrdinalIgnoreCase)) return Auto;
        if (text.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return Chinese;
        if (text.StartsWith("ru", StringComparison.OrdinalIgnoreCase)) return Russian;
        return Chinese;
    }

    /// <summary>首次启动按系统 UI 语言选一次；俄语系统选 ru-RU，其余维持 zh-CN。</summary>
    public static string Resolve(string? configured)
    {
        var normalized = Normalize(configured);
        if (normalized != Auto) return normalized;
        var culture = CultureInfo.CurrentUICulture.Name;
        return culture.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? Russian : Chinese;
    }
}
