using System.Globalization;

namespace VigilWatt.Core.Localization;
/// <summary>
/// 功 能：纯逻辑层文案的可选翻译入口；未注入翻译器时保持中文，老单测断言不受影响。
/// 2026/11/28 dogs0
/// </summary>
public static class UiText
{
    /// <summary>由界面层注入：key 对应 Strings 表中的条目。</summary>
    public static Func<string, string>? Translator { get; set; }

    /// <summary>当前生效语言；生成的 HTML 报告用它标注 lang，默认 zh-CN。</summary>
    public static string Language { get; set; } = AppLanguage.Chinese;

    /// <summary>取文案；没有翻译器（含单测）时用原始中文兜底。</summary>
    public static string Get(string key, string fallbackzh) => Translator?.Invoke(key) ?? fallbackzh;

    /// <summary>带占位符的文案；无翻译器时用中文原文格式化；允许传入尚未查明的空值。</summary>
    public static string Format(string key, string fallbackzh, params object?[]? args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key, fallbackzh), args ?? Array.Empty<object?>());
}
