using VigilWatt.App.Services;
using VigilWatt.Core.Models;
using VigilWatt.Core.Devices;
using VigilWatt.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace VigilWatt.App.ViewModels;

/// <summary>
/// 功 能： 设备列表行（一次性绑定，每次刷新重建；Brush 在 UI 线程的刷新路径里创建）。
/// 2026/10/06 15:53:36 xliu
/// </summary>
public sealed class DeviceRowModel
{
    public string Id { get; init; } = "";
    public string IconGlyph { get; init; } = "\uE702";
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

    public static DeviceRowModel From(DeviceStatus source, AppSettings settings, RemainingTimeEstimateResult remainingTime)
    {
        var d = DevicePresentation.Apply(source, settings);
        var percent = d.BatteryPercent;
        var icon = GlyphFor(d.Kind, d.DisplayName, DevicePresentation.HasIconOverride(d.Id, settings));

        bool charging = DeviceStatusFormatter.IsCharging(d.Charging);
        string state = DeviceStatusFormatter.ChargeSuffix(d.Charging);
        if (!d.IsOnline) state = " · 离线";

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
            IconGlyph = icon,
            Name = d.DisplayName,
            PercentText = DeviceStatusFormatter.PercentText(percent),
            Progress = percent ?? 0,
            SourceText = DeviceSourceInfo.ShortLabel(d.Source),
            SourceBrush = UiPalette.Brush(sourceColor),
            SourceBackgroundBrush = UiPalette.Brush(UiPalette.Tint(sourceColor)),
            SubText = $"{d.Protocol}{state}{(remainingTime.IsAvailable ? $" · 约{RemainingTimeEstimator.Format(remainingTime.Remaining!.Value)}剩余" : $" · {remainingTime.Explain()}")} · 更新于 {d.LastUpdated:HH:mm:ss}",
            IsCharging = charging,
            IsOnline = d.IsOnline,
            ProgressBrush = UiPalette.Brush(progressColor),
            PercentBrush = percentBrush,
        };
    }

    /// <summary>
    /// 设备类型 → 图标字形（Segoe MDL2 Assets / Segoe Fluent Icons）。
    /// **优先用插件声明的 <see cref="DeviceStatus.Kind"/>**，没声明时才按显示名猜——
    /// 真机显示名里一个关键字都没有（"Rapoo Gaming Device"/"MCHOSE 2.4G Wireless"/"EDIFIER W820NB"），
    /// 靠名字猜必然退化成通用蓝牙图标。见 docs/project/handoff.md「第三轮：改显式声明设备类型」。
    /// </summary>
    private static string GlyphFor(DeviceKind kind, string displayName, bool customized)
    {
        if (kind == DeviceKind.Unknown && !customized) kind = DeviceKindGuesser.Guess(displayName);
        return kind switch
        {
            DeviceKind.Gamepad => "\uE7FC",   // Game
            DeviceKind.Mouse => "\uE962",     // Mouse
            DeviceKind.Keyboard => "\uE765",  // KeyboardClassic
            DeviceKind.Headset => "\uE7F6",   // Headphone
            DeviceKind.Speaker => "\uE7F5",   // Speakers
            DeviceKind.Computer => "\uE7F8",  // Devices2（显示器造型，代表本机）
            _ => "\uE702",                    // Bluetooth（兜底）
        };
    }
}
