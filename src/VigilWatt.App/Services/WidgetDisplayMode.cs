using VigilWatt.App.Localization;
namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 桌面小组件的窗口层级：取值定义 + 收敛逻辑。
/// 2026/10/08 17:38:26 xliu
/// 三档语义 —— 置顶（浮在其它窗口上方）、 普通（会被遮挡，默认）、钉桌面（压在其它窗口下方，只有桌面露出来时才可见）。
/// </summary>
public static class WidgetDisplayMode
{
    public const string Topmost = "Topmost";

    public const string Normal = "Normal";

    public const string Desktop = "Desktop";

    /// <summary>把任意输入（含 null / 手改坏了的配置）收敛成三个合法取值之一。</summary>
    public static string Normalize(string? value) => value switch
    {
        Topmost => Topmost,
        Desktop => Desktop,
        _ => Normal,
    };

    /// <summary>设置页副标题用的一句话说明。</summary>
    public static string Describe(string? value) => Normalize(value) switch
    {
        Topmost => Strings.Current.Get("S_bb2865dd66"),
        Desktop => Strings.Current.Get("S_83f0ef83e9"),
        _ => Strings.Current.Get("S_0ca7b17704"),
    };
}

/// <summary>
/// 小组件一屏最多显示几个设备。单独抽出来是为了让"合法范围"只有一处定义：
/// 设置页下拉框按它生成档位，AppSettings 按它收敛手改坏的配置。
/// </summary>
public static class WidgetMaxDevicesOptions
{
    public const int Min = 1;
    public const int Max = 5;
    public const int Default = 3;

    /// <summary>下拉框档位（1~5）。</summary>
    public static IReadOnlyList<int> Choices { get; } = Enumerable.Range(Min, Max - Min + 1).ToArray();

    /// <summary>把任意数值收敛到合法范围。</summary>
    public static int Clamp(int value) => Math.Clamp(value == 0 ? Default : value, Min, Max);
}
