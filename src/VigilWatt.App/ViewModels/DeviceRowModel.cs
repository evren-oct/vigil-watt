using System.ComponentModel;
using System.Runtime.CompilerServices;
using VigilWatt.App.Services;
using VigilWatt.Core.Models;
using VigilWatt.Core.Devices;
using VigilWatt.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace VigilWatt.App.ViewModels;

/// <summary>
/// 功 能： 设备列表行（可变 + 属性通知，原地更新；Brush 在 UI 线程的刷新路径里创建）。
/// 2026/10/06 15:53:36 xliu
/// 不重建对象：替换集合元素会让 ListView 回收容器、整行闪一下（runtime-pitfalls.md §25）；只留扫读四项，DPI/剩余时间在详情页。
/// </summary>
public sealed class DeviceRowModel : INotifyPropertyChanged
{
    /// <summary>不可能被算出来的颜色，只用来表示"这一项还没赋过值"。透明色不会被任何状态色命中。</summary>
    private static readonly Color Unset = Color.FromArgb(0, 0, 0, 0);

    private string _name = "";
    private string _percentText = "----";
    private string _sourceText = "";
    private string _receiverText = "";
    private string? _receiverIconKey;
    private DeviceKind _iconKind;
    private Color _iconColor;
    private Color _sourceColor = Unset;
    private Color _receiverIconColor = Unset;
    private Color _progressColor = Unset;
    // null 是合法值（正常态用主题前景色），所以初始必须给 Unset——否则首帧被判"没变化"，
    // 刷子会停在构造函数里那个灰色，百分比一直是灰的（2026-11-07 回归，见 runtime-pitfalls §25）
    private Color? _percentColor = Unset;
    private double _progress;
    private bool _hasReceiver;
    private bool _isCharging;
    private bool _isOnline = true;
    private Brush _sourceBrush = UiPalette.Brush(UiPalette.Offline);
    private Brush _sourceBackgroundBrush = UiPalette.Brush(UiPalette.Offline);
    private Brush _receiverBackgroundBrush = UiPalette.Brush(UiPalette.Offline);
    private Brush _progressBrush = UiPalette.Brush(UiPalette.Offline);
    private Brush _percentBrush = UiPalette.Brush(UiPalette.Offline);

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; private set; } = "";

    /// <summary>行首图标只存值，由模板里的 <see cref="Controls.DeviceIconPresenter"/> 现画：
    /// 存画好的元素会让复用行把它带进第二个容器（docs/dev/runtime-pitfalls.md §22）。</summary>
    public DeviceKind IconKind { get => _iconKind; private set => Set(ref _iconKind, value); }

    public Color IconColor { get => _iconColor; private set => Set(ref _iconColor, value); }

    public string Name { get => _name; private set => Set(ref _name, value); }

    public string PercentText { get => _percentText; private set => Set(ref _percentText, value); }

    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    public string SourceText { get => _sourceText; private set => Set(ref _sourceText, value); }

    /// <summary>体系标签（Bolt / Unifying）：目录没登记展示名就是空串，行里不占位。</summary>
    public string ReceiverText { get => _receiverText; private set => Set(ref _receiverText, value); }

    /// <summary>体系图标 key：目录声明（wireless / lightning / multi），空值时不画图标。</summary>
    public string? ReceiverIconKey { get => _receiverIconKey; private set => Set(ref _receiverIconKey, value); }

    /// <summary>体系图标描边色：跟链路同色，不另起一套配色。</summary>
    public Color ReceiverIconColor { get => _receiverIconColor; private set => Set(ref _receiverIconColor, value); }

    public bool HasReceiver { get => _hasReceiver; private set => Set(ref _hasReceiver, value); }

    public bool IsCharging { get => _isCharging; private set => Set(ref _isCharging, value); }

    /// <summary>只有离线设备能删：在线设备删掉等于藏起来，插件下一轮又会报回来。</summary>
    public bool IsOnline { get => _isOnline; private set => Set(ref _isOnline, value); }

    /// <summary>连接徽标文字色（有线钢蓝 / 2.4G 紫 / 蓝牙蓝 / 本机青 / 未知灰）。</summary>
    public Brush SourceBrush { get => _sourceBrush; private set => Set(ref _sourceBrush, value); }

    /// <summary>连接徽标胶囊底色（同色压淡）。</summary>
    public Brush SourceBackgroundBrush { get => _sourceBackgroundBrush; private set => Set(ref _sourceBackgroundBrush, value); }

    /// <summary>体系标签底色：与链路同色相、更淡一档——它是链路的从属信息，不另起色相。</summary>
    public Brush ReceiverBackgroundBrush { get => _receiverBackgroundBrush; private set => Set(ref _receiverBackgroundBrush, value); }

    /// <summary>电量条着色：充电蓝 / 低电红 / 中等橙 / 正常绿 / 离线灰。</summary>
    public Brush ProgressBrush { get => _progressBrush; private set => Set(ref _progressBrush, value); }

    /// <summary>百分比文字：仅异常状态着色（充电蓝 / 低电红 / 离线灰），正常态用主题前景色。
    /// 注意 x:Bind 绑 null 会压掉样式默认前景导致文字不可见，此属性必须非空。</summary>
    public Brush PercentBrush { get => _percentBrush; private set => Set(ref _percentBrush, value); }

    /// <summary>
    /// 把一台设备的当前状态写进本行。只对真正变了的字段发通知——轮询每拍都会调它，
    /// 值没变就不该产生任何布局开销。
    /// </summary>
    public void Update(DeviceStatus source, AppSettings settings)
    {
        // 插件没声明类型的按名字猜图标；贴片走同一个方法，两处图标不会再分叉
        var d = DevicePresentation.WithIconKind(DevicePresentation.Apply(source, settings), settings);
        var percent = d.BatteryPercent;
        bool charging = DeviceStatusFormatter.IsCharging(d.Charging);

        Id = d.Id;
        Name = d.DisplayName;
        IconKind = d.Kind;
        IconColor = PrimaryColor;
        // 无读数用 "----"：与 "100%" 同宽，右对齐时不会和相邻行差半个字
        PercentText = DeviceStatusFormatter.PercentText(percent, "----");
        Progress = percent ?? 0;
        // 颜色先就位：体系图标与文本同一拍更新，晚一拍会闪一下透明或旧色
        // 离线徽标必须变灰并标"最后"，不能把历史链路显示成当前连接（M4-3B-2C-4）
        bool online = d.IsOnline;
        SetSourceColor(online ? UiPalette.ConnectionTag(d.Connection, d.Source) : UiPalette.Offline);
        SourceText = DeviceConnectionInfo.Badge(d.Connection, d.Source, online);
        ReceiverText = DeviceConnectionInfo.ReceiverLabel(d.Receiver) ?? "";
        ReceiverIconKey = DeviceConnectionInfo.ReceiverIconKey(d.Receiver);
        HasReceiver = ReceiverText.Length > 0;
        IsCharging = charging;
        IsOnline = online;

        SetProgressColor(online ? UiPalette.BatteryLevel(percent, charging) : UiPalette.Offline);
        // null = 用主题前景色（正常态），非空才换 UiPalette 的固定色
        SetPercentColor(!online ? UiPalette.Offline
            : charging ? UiPalette.Charging
            : percent is <= 20 ? UiPalette.Low
            : null);
    }

    private void SetSourceColor(Color color)
    {
        if (_sourceColor == color) return;
        _sourceColor = color;
        SourceBrush = UiPalette.Brush(color);
        SourceBackgroundBrush = UiPalette.Brush(UiPalette.Tint(color));
        // 体系标签压在链路之下：同色相、约一半浓度，并排时不会抢徽标的注意力
        ReceiverBackgroundBrush = UiPalette.Brush(UiPalette.Tint(color, 0x1A));
        ReceiverIconColor = color;
    }

    private void SetProgressColor(Color color)
    {
        if (_progressColor == color) return;
        _progressColor = color;
        ProgressBrush = UiPalette.Brush(color);
    }

    private void SetPercentColor(Color? color)
    {
        if (Nullable.Equals(_percentColor, color)) return;
        _percentColor = color;
        PercentBrush = color is { } value ? UiPalette.Brush(value) : PrimaryBrush;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Raise(name);
    }

    private void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static Brush PrimaryBrush =>
        Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush ?? UiPalette.Brush(UiPalette.Offline);

    private static Color PrimaryColor =>
        (Application.Current.Resources["TextFillColorPrimaryBrush"] as SolidColorBrush)?.Color
        ?? Color.FromArgb(255, 80, 80, 80);
}
