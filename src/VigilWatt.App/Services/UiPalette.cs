using Microsoft.UI.Xaml.Media;
using VigilWatt.Core.Models;
using Windows.UI;

namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 全局统一状态色板：设备列表、历史图表、桌面小组件、托盘图标共用同一组状态色值。
/// 2026/10/06 15:53:36 xliu
/// 状态色为明暗主题通用的固定值；中性色（文字/卡片底/分隔线）一律用 ThemeResource，不在这里定义。
/// </summary>
public static class UiPalette
{
    public static readonly Color Charging = FromRgb(0x0A, 0x84, 0xFF);
    public static readonly Color Low = FromRgb(0xE2, 0x44, 0x5C);
    public static readonly Color Mid = FromRgb(0xFA, 0xAA, 0x17);
    public static readonly Color Good = FromRgb(0x34, 0xC7, 0x59);
    public static readonly Color Offline = FromRgb(0x8E, 0x8E, 0x93);

    // ---- 来源徽标色（设备列表 + 桌面小组件共用）----
    // 三种来源用三个互不相近的色相，一眼区分：蓝牙=蓝（蓝牙的通用色）/ 2.4G=紫 / 本机=青。
    // 与电量状态色刻意错开（状态色是红/琥珀/绿/蓝/灰，这里避开红绿琥珀，免得"是来源还是电量"分不清）。
    public static readonly Color SourceBluetooth = FromRgb(0x0A, 0x84, 0xFF);
    public static readonly Color Source24G = FromRgb(0xAF, 0x52, 0xDE);
    public static readonly Color SourceSystem = FromRgb(0x30, 0xB0, 0xC7);

    // 有线=钢蓝灰：比蓝牙蓝更暗更灰，不与它抢色相；徽标本身写着「有线」，颜色只做辅助。
    public static readonly Color SourceWired = FromRgb(0x5E, 0x7C, 0x99);

    /// <summary>来源 → 徽标主色（文字色；胶囊底色用 <see cref="Tint"/> 压淡）。</summary>
    public static Color SourceTag(DeviceSource source) => source switch
    {
        DeviceSource.Ble or DeviceSource.BluetoothClassic => SourceBluetooth,
        DeviceSource.System => SourceSystem,
        _ => Source24G,
    };

    /// <summary>徽标取色：连接方式优先，未知才回落来源——与 <see cref="DeviceSourceInfo.BadgeLabel"/> 同一套口径。</summary>
    public static Color ConnectionTag(ConnectionKind connection, DeviceSource source) => connection switch
    {
        ConnectionKind.Wired => SourceWired,
        ConnectionKind.Wireless24G => Source24G,
        _ => SourceTag(source),
    };

    /// <summary>同色半透明底（徽标胶囊背景）。默认约 16% 不透明度——明暗主题下都够淡，不抢电量条。</summary>
    public static Color Tint(Color color, byte alpha = 0x28) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    // ---- 指针遥测标签色（设备列表卡片那行 DPI / 档位 / 回报率）----
    // 只给标签上色、数值保持主题前景色：颜色当"路标"用，别让彩色数字跟右侧大号百分比抢注意力。
    // 三个色相刻意避开来源徽标（蓝/紫/青）与电量状态色（红/琥珀/绿/蓝/灰），取更深的中间调，明暗主题都够对比。
    public static readonly Color MetricDpi = FromRgb(0x1D, 0x9E, 0x75);
    public static readonly Color MetricLevel = FromRgb(0xBA, 0x75, 0x17);
    public static readonly Color MetricRate = FromRgb(0x53, 0x4A, 0xB7);

    /// <summary>指标 → 标签色；未获取的项由调用方改用 <see cref="Offline"/>（灰），不上色。</summary>
    public static Color Metric(PointerMetricKind kind) => kind switch
    {
        PointerMetricKind.Level => MetricLevel,
        PointerMetricKind.Rate => MetricRate,
        _ => MetricDpi,
    };

    /// <summary>多系列图表循环取色（首位为主系列色）。</summary>
    public static readonly Color[] SeriesColors =
    [
        Charging,
        Good,
        Mid,
        FromRgb(0xAF, 0x52, 0xDE),
        FromRgb(0x5E, 0xCE, 0xF1),
        FromRgb(0xFF, 0x9F, 0x0A),
    ];

    /// <summary>电量状态 → 状态色。离线（IsOnline=false）由调用方直接用 Offline。</summary>
    public static Color BatteryLevel(int? percent, bool charging)
    {
        if (charging) return Charging;
        return percent switch
        {
            null => Offline,
            <= 20 => Low,
            <= 50 => Mid,
            _ => Good,
        };
    }

    public static SolidColorBrush Brush(Color color) => new(color);

    /// <summary>托盘图标走 GDI+，需要 System.Drawing 的颜色类型。</summary>
    public static System.Drawing.Color ToDrawing(Color color) =>
        System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);

    private static Color FromRgb(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);
}
