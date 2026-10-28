using VigilWatt.App.Services;
using VigilWatt.Core.Models;
using VigilWatt.Core.Devices;
using VigilWatt.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace VigilWatt.App.ViewModels;

/// <summary>
/// 功 能： 设备列表行（一次性绑定，每次刷新重建；Brush 在 UI 线程的刷新路径里创建）。
/// 2026/10/06 15:53:36 xliu
/// </summary>
public sealed class DeviceRowModel
{
    public string Id { get; init; } = "";

    /// <summary>行首图标只存值，由模板里的 <see cref="Controls.DeviceIconPresenter"/> 现画：
    /// 存画好的元素会让复用行把它带进第二个容器（docs/dev/runtime-pitfalls.md §22）。</summary>
    public DeviceKind IconKind { get; init; }

    public Color IconColor { get; init; }

    public string Name { get; init; } = "";
    public string PercentText { get; init; } = "--%";
    public double Progress { get; init; }
    public string SourceText { get; init; } = "";
    public string SubText { get; init; } = "";
    public bool IsCharging { get; init; }

    /// <summary>只有离线设备能删：在线设备删掉等于藏起来，插件下一轮又会报回来。</summary>
    public bool IsOnline { get; init; } = true;

    /// <summary>来源徽标文字色（蓝牙蓝 / 2.4G 紫 / 本机青）。</summary>
    public Brush SourceBrush { get; init; } = UiPalette.Brush(UiPalette.Offline);

    /// <summary>来源徽标胶囊底色（同色压淡）。</summary>
    public Brush SourceBackgroundBrush { get; init; } = UiPalette.Brush(UiPalette.Offline);

    /// <summary>电量条着色：充电蓝 / 低电红 / 中等橙 / 正常绿 / 离线灰。</summary>
    public Brush ProgressBrush { get; init; } = UiPalette.Brush(UiPalette.Offline);

    /// <summary>百分比文字：仅异常状态着色（充电蓝 / 低电红 / 离线灰），正常态用主题前景色。
    /// 注意 x:Bind 绑 null 会压掉样式默认前景导致文字不可见，此属性必须非空。</summary>
    public Brush PercentBrush { get; init; } = UiPalette.Brush(UiPalette.Offline);

    /// <summary>显示内容的指纹：刷新时指纹相同就整行复用，避免每分钟轮询把列表重建一遍。</summary>
    public string Signature { get; init; } = "";

    public static DeviceRowModel From(
        DeviceStatus source, AppSettings settings, RemainingTimeEstimateResult remainingTime,
        DeviceRowModel? previous = null)
    {
        // 插件没声明类型的按名字猜图标；贴片走同一个方法，两处图标不会再分叉
        var d = DevicePresentation.WithIconKind(DevicePresentation.Apply(source, settings), settings);
        var percent = d.BatteryPercent;
        var kind = d.Kind;

        bool charging = DeviceStatusFormatter.IsCharging(d.Charging);
        string state = DeviceStatusFormatter.ChargeSuffix(d.Charging);
        if (!d.IsOnline) state = " · 离线";

        // 无读数用 "----"：与 "100%" 同宽，右对齐时不会和相邻行差半个字
        string percentText = DeviceStatusFormatter.PercentText(percent, "----");
        string sourceText = DeviceSourceInfo.ShortLabel(d.Source);
        string subText = $"{d.Protocol}{state}{(remainingTime.IsAvailable ? $" · 约{RemainingTimeEstimator.Format(remainingTime.Remaining!.Value)}剩余" : $" · {remainingTime.Explain()}")} · 更新于 {d.LastUpdated:HH:mm:ss}";

        // Source 也要进指纹：Ble 与 BluetoothClassic 的徽标文字都是"蓝牙"，但颜色不同
        string signature = string.Join('|',
            d.Id, d.DisplayName, (int)kind, percentText, subText, sourceText, (int)d.Source, charging, d.IsOnline);

        if (previous != null && previous.Signature == signature) return previous;

        var iconColor = (Application.Current.Resources["TextFillColorPrimaryBrush"] as SolidColorBrush)?.Color
                        ?? Color.FromArgb(255, 80, 80, 80);
        var progressColor = d.IsOnline
            ? UiPalette.BatteryLevel(percent, charging)
            : UiPalette.Offline;
        var sourceColor = UiPalette.SourceTag(d.Source);
        Brush percentBrush = !d.IsOnline ? UiPalette.Brush(UiPalette.Offline)
            : charging ? UiPalette.Brush(UiPalette.Charging)
            : percent is <= 20 ? UiPalette.Brush(UiPalette.Low)
            : Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush
              ?? UiPalette.Brush(UiPalette.Offline);

        return new DeviceRowModel
        {
            Id = d.Id,
            Signature = signature,
            IconKind = kind,
            IconColor = iconColor,
            Name = d.DisplayName,
            PercentText = percentText,
            Progress = percent ?? 0,
            SourceText = sourceText,
            SourceBrush = UiPalette.Brush(sourceColor),
            SourceBackgroundBrush = UiPalette.Brush(UiPalette.Tint(sourceColor)),
            SubText = subText,
            IsCharging = charging,
            IsOnline = d.IsOnline,
            ProgressBrush = UiPalette.Brush(progressColor),
            PercentBrush = percentBrush,
        };
    }
}
