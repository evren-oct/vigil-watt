using VigilWatt.Core.Utils;

using VigilWatt.App.Localization;
namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 设置页「日志级别」下拉框的展示与说明。
/// 2026/10/09 10:13:25 xliu
/// 取值定义在 Core 的 <see cref="LogLevelNames"/> （**单一来源**），这里只负责"取值 → 中文标签 / 一句话说明"。
/// </summary>
public static class LogLevelOptions
{
    /// <summary>下拉框档位，顺序与展示一致：关闭 → 详细 → 信息 → 警告 → 错误。</summary>
    public static IReadOnlyList<(string Value, string Label)> Choices { get; } = new[]
    {
        (LogLevelNames.Off, Strings.Current.Get("S_6c14bd7f6f")),
        (LogLevelNames.Debug, Strings.Current.Get("S_613f7a44e2")),
        (LogLevelNames.Info, Strings.Current.Get("S_2da40f4073")),
        (LogLevelNames.Warn, Strings.Current.Get("S_5521e368d8")),
        (LogLevelNames.Error, Strings.Current.Get("S_b859c7be75")),
    };

    /// <summary>取值 → 中文标签。</summary>
    public static string Label(string? value)
    {
        var v = LogLevelNames.Normalize(value);
        foreach (var (val, label) in Choices)
            if (val == v) return label;
        return Strings.Current.Get("S_2da40f4073");
    }

    /// <summary>设置页副标题：说明当前档位意味着什么（统一带"需重启生效"）。</summary>
    public static string Describe(string? value) => LogLevelNames.Normalize(value) switch
    {
        LogLevelNames.Off => Strings.Current.Get("S_a923dc8ff7"),
        LogLevelNames.Debug => Strings.Current.Get("S_448e2dcd51"),
        LogLevelNames.Warn => Strings.Current.Get("S_f259d265d3"),
        LogLevelNames.Error => Strings.Current.Get("S_3797e8cd1f"),
        _ => Strings.Current.Get("S_7e9baf0ac8"),
    };
}
