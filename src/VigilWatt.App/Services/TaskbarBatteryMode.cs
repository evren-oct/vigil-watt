using VigilWatt.Core.Localization;

namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 任务栏电量贴片的显示内容：取值定义 + 收敛逻辑。
/// 2026/10/10 21:34:00 xliu
/// 两档语义 —— 全部（每台在线设备一格）、 最低（只显示电量最低的一台）。
/// </summary>
public static class TaskbarBatteryMode
{
    public const string All = "All";

    public const string Lowest = "Lowest";

    /// <summary>把任意输入（含 null / 手改坏了的配置）收敛成两个合法取值之一。</summary>
    public static string Normalize(string? value) => value switch
    {
        Lowest => Lowest,
        _ => All,
    };

    /// <summary>设置页副标题用的一句话说明。</summary>
    public static string Describe(string? value) => Normalize(value) switch
    {
        Lowest => UiText.Get("S_562088c9a7", "只显示电量最低的一台，贴片最窄"),
        _ => UiText.Get("S_1b3ec1f7b0", "每台在线设备一格，按电量从低到高排列"),
    };
}

/// <summary>
/// 任务栏贴片最多显示几台设备。与 <see cref="WidgetMaxDevicesOptions"/> 范围相同但**各自独立定义**：
/// 两个功能的默认值/上限将来可能分开演进（贴片窄、小组件宽），共用一个常量反而会互相牵制。
/// </summary>
public static class TaskbarBatteryOptions
{
    public const int Min = 1;
    public const int Max = 5;
    public const int Default = 3;

    /// <summary>下拉框档位（1~5）。</summary>
    public static IReadOnlyList<int> Choices { get; } = Enumerable.Range(Min, Max - Min + 1).ToArray();

    /// <summary>把任意数值收敛到合法范围。</summary>
    public static int Clamp(int value) => Math.Clamp(value == 0 ? Default : value, Min, Max);
}
